# Web Portal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 MVP tenant-admin web portal — login, employee
management, basic shift assignment, and a daily attendance view — that is
usable from a phone browser and calls the backend API contract directly.

**Architecture:** Next.js (App Router) with Tailwind CSS. Pages that only
read data are React Server Components calling the backend directly
(reading the session token server-side); every mutation (login, create/
delete employee, create/delete shift) is a React Server Action — a plain
`"use server"` async function wired straight to a `<form action={...}>`,
no hand-rolled API routes or client-side fetch plumbing needed. Session
state is a single httpOnly cookie holding the backend's JWT; `middleware.ts`
gates the dashboard routes on its presence.

**Tech Stack:** Next.js 14 (App Router, TypeScript), Tailwind CSS, Vitest +
@testing-library/react for tests. Recharts (per the project's recommended
stack) is intentionally not used in Phase 1 — the daily attendance view is
a plain table; charts belong to Phase 3's reporting depth.

**Depends on:** [backend-api plan](2026-08-15-backend-api.md) — this
portal calls `POST /api/auth/login`, `GET/POST/DELETE /api/employees`,
`GET/POST/DELETE /api/shifts`, and `GET /api/attendance/daily?date=`, all
authenticated with the `Bearer` JWT that login returns for a `TenantAdmin`
user.

## Global Constraints

- Mobile-friendly, phone-browser access — every page must render usably
  down to a ~375px viewport: wrapping nav, `overflow-x-auto` around wide
  tables, no fixed-width layouts.
- The JWT is never exposed to client-side JavaScript — it lives only in
  an httpOnly cookie set by the `login` Server Action and read server-side
  by `getToken()`. No `localStorage`/`sessionStorage` token storage.
- No payment/billing UI in Phase 1 — tenant status is operator-only and
  has no portal surface here.
- Reporting is web-portal only for Phase 1 — no digest emails, no
  WhatsApp/SMS.

---

## File Structure

```
portal/
  package.json
  next.config.mjs
  tailwind.config.ts
  vitest.config.ts
  vitest.setup.ts
  middleware.ts
  src/
    lib/
      constants.ts
      session.ts
      backendFetch.ts
      types.ts
    app/
      layout.tsx
      globals.css
      login/
        page.tsx
        actions.ts
      (dashboard)/
        layout.tsx
        actions.ts
        shifts/
          page.tsx
          actions.ts
          ShiftForm.tsx
        employees/
          page.tsx
          actions.ts
          EmployeeForm.tsx
        attendance/
          page.tsx
          DateNav.tsx
  README.md
```

---

### Task 1: Scaffold + backend fetch helper + shared types

**Files:**
- Create: `portal/package.json` (via `create-next-app`)
- Create: `portal/vitest.config.ts`
- Create: `portal/vitest.setup.ts`
- Create: `portal/src/lib/constants.ts`
- Create: `portal/src/lib/backendFetch.ts`
- Create: `portal/src/lib/types.ts`
- Test: `portal/src/lib/backendFetch.test.ts`

**Interfaces:**
- Produces: `backendFetch(path: string, init?: RequestInit & { token?:
  string }) -> Promise<any>` and `class BackendError extends Error {
  status: number }`, reused by every Server Action and Server Component
  page in this plan; `SESSION_COOKIE` constant, reused by `session.ts`
  and `middleware.ts`; shared TypeScript types `EmployeeResponse`,
  `ShiftResponse`, `DailyAttendanceResponse` matching the backend plan's
  DTOs.

- [ ] **Step 1: Scaffold the Next.js app and testing tools**

```bash
npx create-next-app@latest portal --typescript --tailwind --eslint --app --src-dir --import-alias "@/*" --use-npm
cd portal
npm install -D vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event
```

Add a `test` script to `package.json`'s `"scripts"`:

```json
"test": "vitest run"
```

- [ ] **Step 2: Write the Vitest config**

```typescript
// portal/vitest.config.ts
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    globals: true,
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
});
```

```typescript
// portal/vitest.setup.ts
import "@testing-library/jest-dom/vitest";
```

- [ ] **Step 3: Write shared constants and types**

```typescript
// portal/src/lib/constants.ts
export const SESSION_COOKIE = "zak_session";
```

```typescript
// portal/src/lib/types.ts
export type EmployeeResponse = {
  id: string;
  employeeCode: string;
  name: string;
  shiftId: string | null;
};

export type ShiftResponse = {
  id: string;
  name: string;
  startTime: string;
  endTime: string;
  graceMinutes: number;
  punchMode: "TwoPunch" | "FourPunch";
  breakStart: string | null;
  breakEnd: string | null;
  allowedBreakMinutes: number | null;
};

export type DailyAttendanceResponse = {
  employeeId: string;
  employeeName: string;
  firstIn: string | null;
  lastOut: string | null;
};

export type LoginResult = {
  token: string;
  role: "Operator" | "TenantAdmin";
  tenantId: string | null;
};
```

- [ ] **Step 4: Write the failing `backendFetch` tests**

```typescript
// portal/src/lib/backendFetch.test.ts
import { describe, it, expect, vi } from "vitest";
import { backendFetch, BackendError } from "./backendFetch";

describe("backendFetch", () => {
  it("attaches an Authorization header when a token is provided", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await backendFetch("/api/employees", { token: "jwt-abc" });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get("Authorization")).toBe("Bearer jwt-abc");
  });

  it("throws BackendError carrying the response status on failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("nope", { status: 403 })));

    await expect(backendFetch("/api/employees")).rejects.toBeInstanceOf(BackendError);
  });

  it("returns null for a 204 No Content response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await backendFetch("/api/employees/123", { method: "DELETE", token: "jwt-abc" });

    expect(result).toBeNull();
  });
});
```

- [ ] **Step 5: Run test to verify it fails**

Run: `npm --prefix portal test -- backendFetch`
Expected: FAIL — compile error, `backendFetch`/`BackendError` don't exist.

- [ ] **Step 6: Implement `backendFetch`**

```typescript
// portal/src/lib/backendFetch.ts
const BACKEND_URL = process.env.BACKEND_API_URL ?? "http://localhost:8080";

export class BackendError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function backendFetch(
  path: string,
  init: RequestInit & { token?: string } = {},
): Promise<any> {
  const { token, ...rest } = init;
  const headers = new Headers(rest.headers);
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${BACKEND_URL}${path}`, { ...rest, headers, cache: "no-store" });

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new BackendError(response.status, text || response.statusText);
  }
  if (response.status === 204) return null;
  return response.json();
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `npm --prefix portal test -- backendFetch`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add portal
git commit -m "feat: scaffold portal with backend fetch helper and shared types"
```

---

### Task 2: Login + session cookie + route protection

**Files:**
- Create: `portal/src/lib/session.ts`
- Create: `portal/src/app/login/actions.ts`
- Create: `portal/src/app/login/page.tsx`
- Create: `portal/middleware.ts`
- Test: `portal/src/app/login/actions.test.ts`
- Test: `portal/middleware.test.ts`

**Interfaces:**
- Consumes: `backendFetch`/`BackendError` (Task 1), `SESSION_COOKIE` (Task 1).
- Produces: `getToken() -> string | undefined`, used by every later
  Server Component page and Server Action in this plan; `login(formData:
  FormData) -> Promise<void>` (redirects, never returns a value on the
  happy path); `middleware` gating `/attendance`, `/employees`, `/shifts`.

- [ ] **Step 1: Write `session.ts`**

```typescript
// portal/src/lib/session.ts
import { cookies } from "next/headers";
import { SESSION_COOKIE } from "./constants";

export function getToken(): string | undefined {
  return cookies().get(SESSION_COOKIE)?.value;
}
```

- [ ] **Step 2: Write the failing login action test**

```typescript
// portal/src/app/login/actions.test.ts
import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
    _store: store,
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";
import { login } from "./actions";

describe("login action", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore._store.clear();
  });

  it("sets the session cookie and redirects to /attendance on success", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ token: "jwt-abc", role: "TenantAdmin", tenantId: "t1" }), { status: 200 }),
      ),
    );
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "correct-horse");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)?.value).toBe("jwt-abc");
    expect(redirect).toHaveBeenCalledWith("/attendance");
  });

  it("redirects to /login?error=1 without setting a cookie on invalid credentials", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("nope", { status: 401 })));
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "wrong");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login?error=1");
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `npm --prefix portal test -- login/actions`
Expected: FAIL — compile error, `login` action doesn't exist.

- [ ] **Step 4: Implement the login action**

```typescript
// portal/src/app/login/actions.ts
"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { backendFetch } from "@/lib/backendFetch";
import { SESSION_COOKIE } from "@/lib/constants";
import type { LoginResult } from "@/lib/types";

export async function login(formData: FormData) {
  const email = String(formData.get("email") ?? "");
  const password = String(formData.get("password") ?? "");

  try {
    const result: LoginResult = await backendFetch("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    cookies().set(SESSION_COOKIE, result.token, {
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      maxAge: 60 * 60 * 12,
    });
  } catch {
    redirect("/login?error=1");
    return;
  }

  redirect("/attendance");
}
```

`redirect()` isn't wrapped a second time inside the `catch` block itself,
so its real-runtime throw propagates normally; the explicit `return`
right after it exists only so the mocked `redirect` in tests (a no-op
`vi.fn()`) doesn't fall through into the success-path `redirect` call
below.

- [ ] **Step 5: Run test to verify it passes**

Run: `npm --prefix portal test -- login/actions`
Expected: PASS

- [ ] **Step 6: Write the login page**

```tsx
// portal/src/app/login/page.tsx
import { login } from "./actions";

export default function LoginPage({ searchParams }: { searchParams: { error?: string } }) {
  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <form action={login} className="w-full max-w-sm space-y-4 border rounded p-6">
        <h1 className="text-xl font-semibold">ZAK Attendance — Sign in</h1>
        {searchParams.error && <p className="text-red-600 text-sm">Invalid email or password.</p>}
        <label className="flex flex-col text-sm gap-1">
          Email
          <input type="email" name="email" required className="border rounded px-2 py-1" />
        </label>
        <label className="flex flex-col text-sm gap-1">
          Password
          <input type="password" name="password" required className="border rounded px-2 py-1" />
        </label>
        <button type="submit" className="w-full bg-blue-600 text-white rounded px-3 py-2">
          Sign in
        </button>
      </form>
    </div>
  );
}
```

- [ ] **Step 7: Write the failing middleware test**

```typescript
// portal/middleware.test.ts
import { describe, it, expect } from "vitest";
import { NextRequest } from "next/server";
import { middleware } from "./middleware";

describe("middleware", () => {
  it("redirects unauthenticated requests to protected paths to /login", () => {
    const request = new NextRequest("http://localhost/employees");

    const response = middleware(request);

    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("http://localhost/login");
  });

  it("passes through authenticated requests to protected paths", () => {
    const request = new NextRequest("http://localhost/employees", {
      headers: { Cookie: "zak_session=jwt-abc" },
    });

    const response = middleware(request);

    expect(response.status).toBe(200);
  });

  it("passes through unprotected paths without a session", () => {
    const request = new NextRequest("http://localhost/login");

    const response = middleware(request);

    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 8: Run test to verify it fails**

Run: `npm --prefix portal test -- middleware`
Expected: FAIL — compile error, `middleware` doesn't exist.

- [ ] **Step 9: Implement `middleware.ts`**

```typescript
// portal/middleware.ts
import { NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE } from "@/lib/constants";

const PROTECTED_PREFIXES = ["/attendance", "/employees", "/shifts"];

export function middleware(request: NextRequest) {
  const isProtected = PROTECTED_PREFIXES.some((prefix) => request.nextUrl.pathname.startsWith(prefix));
  if (!isProtected) return NextResponse.next();

  if (!request.cookies.has(SESSION_COOKIE)) {
    return NextResponse.redirect(new URL("/login", request.url));
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/attendance/:path*", "/employees/:path*", "/shifts/:path*"],
};
```

- [ ] **Step 10: Run test to verify it passes**

Run: `npm --prefix portal test -- middleware`
Expected: PASS

- [ ] **Step 11: Commit**

```bash
git add portal
git commit -m "feat: add login Server Action, session cookie, and route protection middleware"
```

---

### Task 3: Shifts page

**Files:**
- Create: `portal/src/app/(dashboard)/shifts/actions.ts`
- Create: `portal/src/app/(dashboard)/shifts/page.tsx`
- Create: `portal/src/app/(dashboard)/shifts/ShiftForm.tsx`
- Test: `portal/src/app/(dashboard)/shifts/actions.test.ts`

**Interfaces:**
- Consumes: `backendFetch` (Task 1), `getToken()` (Task 2).
- Produces: `createShift(formData: FormData) -> Promise<void>`,
  `deleteShift(id: string) -> Promise<void>` — the `ShiftResponse[]`
  fetched here is also consumed by Task 4's employee shift dropdown.

- [ ] **Step 1: Write the failing shift action tests**

```typescript
// portal/src/app/(dashboard)/shifts/actions.test.ts
import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));

import { revalidatePath } from "next/cache";
import { SESSION_COOKIE } from "@/lib/constants";
import { createShift, deleteShift } from "./actions";

describe("shift actions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("posts shift data and revalidates the shifts page", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "s1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("name", "Day Shift");
    formData.set("startTime", "09:00:00");
    formData.set("endTime", "17:00:00");
    formData.set("graceMinutes", "10");
    formData.set("punchMode", "TwoPunch");

    await createShift(formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({ name: "Day Shift", startTime: "09:00:00", punchMode: "TwoPunch" });
    expect(revalidatePath).toHaveBeenCalledWith("/shifts");
  });

  it("deletes a shift by id and revalidates", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteShift("s1");

    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/shifts/s1");
    expect(revalidatePath).toHaveBeenCalledWith("/shifts");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm --prefix portal test -- shifts/actions`
Expected: FAIL — compile error, `createShift`/`deleteShift` don't exist.

- [ ] **Step 3: Implement the shift actions**

```typescript
// portal/src/app/(dashboard)/shifts/actions.ts
"use server";

import { revalidatePath } from "next/cache";
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export async function createShift(formData: FormData) {
  const breakStart = formData.get("breakStart");
  const breakEnd = formData.get("breakEnd");
  const allowedBreakMinutes = formData.get("allowedBreakMinutes");

  await backendFetch("/api/shifts", {
    method: "POST",
    token: getToken(),
    body: JSON.stringify({
      name: String(formData.get("name")),
      startTime: String(formData.get("startTime")),
      endTime: String(formData.get("endTime")),
      graceMinutes: Number(formData.get("graceMinutes") ?? 0),
      punchMode: String(formData.get("punchMode")),
      breakStart: breakStart ? String(breakStart) : null,
      breakEnd: breakEnd ? String(breakEnd) : null,
      allowedBreakMinutes: allowedBreakMinutes ? Number(allowedBreakMinutes) : null,
    }),
  });
  revalidatePath("/shifts");
}

export async function deleteShift(id: string) {
  await backendFetch(`/api/shifts/${id}`, { method: "DELETE", token: getToken() });
  revalidatePath("/shifts");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm --prefix portal test -- shifts/actions`
Expected: PASS

- [ ] **Step 5: Write the shift form and page**

Time inputs use `step={1}` so their value includes seconds
(`HH:mm:ss`), matching the format the backend's `TimeOnly` JSON
converter expects — without it, a plain `<input type="time">` yields
`HH:mm` and the backend request would fail to parse.

```tsx
// portal/src/app/(dashboard)/shifts/ShiftForm.tsx
"use client";

import { createShift } from "./actions";

export function ShiftForm() {
  return (
    <form action={createShift} className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-6 items-end">
      <label className="flex flex-col text-sm gap-1">
        Name
        <input name="name" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Start
        <input type="time" step={1} name="startTime" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        End
        <input type="time" step={1} name="endTime" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Grace (min)
        <input type="number" name="graceMinutes" defaultValue={0} className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Punch mode
        <select name="punchMode" className="border rounded px-2 py-1">
          <option value="TwoPunch">2-punch (IN/OUT)</option>
          <option value="FourPunch">4-punch (+ breaks)</option>
        </select>
      </label>
      <button type="submit" className="bg-blue-600 text-white rounded px-3 py-1">
        Add shift
      </button>
    </form>
  );
}
```

```tsx
// portal/src/app/(dashboard)/shifts/page.tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { ShiftResponse } from "@/lib/types";
import { ShiftForm } from "./ShiftForm";
import { deleteShift } from "./actions";

export default async function ShiftsPage() {
  const shifts: ShiftResponse[] = await backendFetch("/api/shifts", { token: getToken() });

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Shifts</h1>
      <ShiftForm />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Name</th>
              <th>Start</th>
              <th>End</th>
              <th>Grace (min)</th>
              <th>Mode</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((shift) => (
              <tr key={shift.id} className="border-b">
                <td className="py-2">{shift.name}</td>
                <td>{shift.startTime}</td>
                <td>{shift.endTime}</td>
                <td>{shift.graceMinutes}</td>
                <td>{shift.punchMode}</td>
                <td>
                  <form action={deleteShift.bind(null, shift.id)}>
                    <button type="submit" className="text-red-600">
                      Delete
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Commit**

```bash
git add portal
git commit -m "feat: add shifts page with create/delete Server Actions"
```

---

### Task 4: Employees page

**Files:**
- Create: `portal/src/app/(dashboard)/employees/actions.ts`
- Create: `portal/src/app/(dashboard)/employees/page.tsx`
- Create: `portal/src/app/(dashboard)/employees/EmployeeForm.tsx`
- Test: `portal/src/app/(dashboard)/employees/actions.test.ts`
- Test: `portal/src/app/(dashboard)/employees/EmployeeForm.test.tsx`

**Interfaces:**
- Consumes: `backendFetch`/`getToken` (Tasks 1-2), `ShiftResponse` (Task 3).
- Produces: `createEmployee(formData: FormData) -> Promise<void>`,
  `deleteEmployee(id: string) -> Promise<void>`.

- [ ] **Step 1: Write the failing employee action tests**

```typescript
// portal/src/app/(dashboard)/employees/actions.test.ts
import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));

import { revalidatePath } from "next/cache";
import { SESSION_COOKIE } from "@/lib/constants";
import { createEmployee, deleteEmployee } from "./actions";

describe("employee actions", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("posts employee data with a null shiftId when none is selected", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "e1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");

    await createEmployee(formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body).toEqual({ employeeCode: "E001", name: "Jane Doe", shiftId: null });
    expect(revalidatePath).toHaveBeenCalledWith("/employees");
  });

  it("posts the selected shiftId when provided", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "e1" }), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");
    formData.set("shiftId", "s1");

    await createEmployee(formData);

    const [, init] = fetchMock.mock.calls[0];
    const body = JSON.parse(init.body as string);
    expect(body.shiftId).toBe("s1");
  });

  it("deletes an employee by id and revalidates", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await deleteEmployee("e1");

    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/employees/e1");
    expect(revalidatePath).toHaveBeenCalledWith("/employees");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm --prefix portal test -- employees/actions`
Expected: FAIL — compile error, `createEmployee`/`deleteEmployee` don't exist.

- [ ] **Step 3: Implement the employee actions**

```typescript
// portal/src/app/(dashboard)/employees/actions.ts
"use server";

import { revalidatePath } from "next/cache";
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";

export async function createEmployee(formData: FormData) {
  const shiftId = formData.get("shiftId");

  await backendFetch("/api/employees", {
    method: "POST",
    token: getToken(),
    body: JSON.stringify({
      employeeCode: String(formData.get("employeeCode")),
      name: String(formData.get("name")),
      shiftId: shiftId ? String(shiftId) : null,
    }),
  });
  revalidatePath("/employees");
}

export async function deleteEmployee(id: string) {
  await backendFetch(`/api/employees/${id}`, { method: "DELETE", token: getToken() });
  revalidatePath("/employees");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm --prefix portal test -- employees/actions`
Expected: PASS

- [ ] **Step 5: Write the failing EmployeeForm component test**

```tsx
// portal/src/app/(dashboard)/employees/EmployeeForm.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { EmployeeForm } from "./EmployeeForm";

describe("EmployeeForm", () => {
  it("renders a shift option for each provided shift", () => {
    render(
      <EmployeeForm
        shifts={[
          {
            id: "s1",
            name: "Day Shift",
            startTime: "09:00:00",
            endTime: "17:00:00",
            graceMinutes: 10,
            punchMode: "TwoPunch",
            breakStart: null,
            breakEnd: null,
            allowedBreakMinutes: null,
          },
        ]}
      />,
    );

    expect(screen.getByRole("option", { name: "Day Shift" })).toBeInTheDocument();
  });

  it("requires employee code and name fields", () => {
    render(<EmployeeForm shifts={[]} />);

    expect(screen.getByLabelText(/employee code/i)).toBeRequired();
    expect(screen.getByLabelText(/^name$/i)).toBeRequired();
  });
});
```

- [ ] **Step 6: Run test to verify it fails**

Run: `npm --prefix portal test -- EmployeeForm`
Expected: FAIL — compile error, `EmployeeForm` doesn't exist.

- [ ] **Step 7: Implement `EmployeeForm` and the page**

```tsx
// portal/src/app/(dashboard)/employees/EmployeeForm.tsx
"use client";

import type { ShiftResponse } from "@/lib/types";
import { createEmployee } from "./actions";

export function EmployeeForm({ shifts }: { shifts: ShiftResponse[] }) {
  return (
    <form action={createEmployee} className="grid grid-cols-2 gap-2 sm:grid-cols-4 items-end">
      <label htmlFor="employeeCode" className="flex flex-col text-sm gap-1">
        Employee code
        <input id="employeeCode" name="employeeCode" required className="border rounded px-2 py-1" />
      </label>
      <label htmlFor="name" className="flex flex-col text-sm gap-1">
        Name
        <input id="name" name="name" required className="border rounded px-2 py-1" />
      </label>
      <label htmlFor="shiftId" className="flex flex-col text-sm gap-1">
        Shift
        <select id="shiftId" name="shiftId" className="border rounded px-2 py-1">
          <option value="">— None —</option>
          {shifts.map((shift) => (
            <option key={shift.id} value={shift.id}>
              {shift.name}
            </option>
          ))}
        </select>
      </label>
      <button type="submit" className="bg-blue-600 text-white rounded px-3 py-1">
        Add employee
      </button>
    </form>
  );
}
```

```tsx
// portal/src/app/(dashboard)/employees/page.tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { EmployeeForm } from "./EmployeeForm";
import { deleteEmployee } from "./actions";

export default async function EmployeesPage() {
  const token = getToken();
  const [employees, shifts]: [EmployeeResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch("/api/employees", { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Employees</h1>
      <EmployeeForm shifts={shifts} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Code</th>
              <th>Name</th>
              <th>Shift</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.id} className="border-b">
                <td className="py-2">{employee.employeeCode}</td>
                <td>{employee.name}</td>
                <td>{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td>
                  <form action={deleteEmployee.bind(null, employee.id)}>
                    <button type="submit" className="text-red-600">
                      Delete
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `npm --prefix portal test -- EmployeeForm`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add portal
git commit -m "feat: add employees page with shift assignment"
```

---

### Task 5: Attendance view + dashboard shell + logout

**Files:**
- Create: `portal/src/app/(dashboard)/attendance/page.tsx`
- Create: `portal/src/app/(dashboard)/attendance/DateNav.tsx`
- Create: `portal/src/app/(dashboard)/actions.ts`
- Create: `portal/src/app/(dashboard)/layout.tsx`
- Test: `portal/src/app/(dashboard)/actions.test.ts`

**Interfaces:**
- Consumes: `backendFetch`/`getToken` (Tasks 1-2), `DailyAttendanceResponse`
  (Task 1).
- Produces: `logout() -> Promise<void>`, the dashboard nav shell wrapping
  all three pages built in Tasks 3-5.

- [ ] **Step 1: Write the failing logout action test**

```typescript
// portal/src/app/(dashboard)/actions.test.ts
import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
    delete: (name: string) => {
      store.delete(name);
    },
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";
import { logout } from "./actions";

describe("logout action", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("clears the session cookie and redirects to /login", async () => {
    await logout();

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm --prefix portal test -- "(dashboard)/actions"`
Expected: FAIL — compile error, `logout` doesn't exist.

- [ ] **Step 3: Implement `logout`**

```typescript
// portal/src/app/(dashboard)/actions.ts
"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";

export async function logout() {
  cookies().delete(SESSION_COOKIE);
  redirect("/login");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm --prefix portal test -- "(dashboard)/actions"`
Expected: PASS

- [ ] **Step 5: Write the attendance page, date nav, and dashboard layout**

```tsx
// portal/src/app/(dashboard)/attendance/DateNav.tsx
"use client";

import { useRouter } from "next/navigation";

export function DateNav({ date }: { date: string }) {
  const router = useRouter();

  return (
    <input
      type="date"
      defaultValue={date}
      onChange={(e) => router.push(`/attendance?date=${e.target.value}`)}
      className="border rounded px-2 py-1"
    />
  );
}
```

```tsx
// portal/src/app/(dashboard)/attendance/page.tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { DailyAttendanceResponse } from "@/lib/types";
import { DateNav } from "./DateNav";

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

export default async function AttendancePage({ searchParams }: { searchParams: { date?: string } }) {
  const date = searchParams.date ?? todayIsoDate();
  const rows: DailyAttendanceResponse[] = await backendFetch(`/api/attendance/daily?date=${date}`, {
    token: getToken(),
  });

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Employee</th>
              <th>First In</th>
              <th>Last Out</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b">
                <td className="py-2">{row.employeeName}</td>
                <td>{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td>{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

```tsx
// portal/src/app/(dashboard)/layout.tsx
import Link from "next/link";
import { logout } from "./actions";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col">
      <nav className="flex flex-wrap items-center gap-4 border-b p-4">
        <Link href="/attendance">Attendance</Link>
        <Link href="/employees">Employees</Link>
        <Link href="/shifts">Shifts</Link>
        <form action={logout} className="ml-auto">
          <button type="submit" className="text-sm text-gray-500">
            Log out
          </button>
        </form>
      </nav>
      <main className="flex-1">{children}</main>
    </div>
  );
}
```

- [ ] **Step 6: Commit**

```bash
git add portal
git commit -m "feat: add daily attendance view, dashboard nav shell, and logout"
```

---

### Task 6: Manual browser verification + README

**Files:**
- Create: `portal/README.md`
- Create: `portal/.env.local.example`

**Interfaces:**
- Consumes: everything above.
- Produces: a documented, manually-verified end-to-end flow (this task's
  checklist cannot be automated — it requires a running backend and a
  real browser).

- [ ] **Step 1: Write `portal/.env.local.example`**

```
BACKEND_API_URL=http://localhost:8080
```

- [ ] **Step 2: Write `portal/README.md`**

```markdown
# Attendance Web Portal

## Local development

1. Copy `.env.local.example` to `.env.local` and point `BACKEND_API_URL`
   at a running instance of the backend API.
2. `npm install`
3. `npm run dev` — the portal runs at `http://localhost:3000`.

## Running tests

`npm test`

Server Actions and middleware are tested directly (no browser needed) by
mocking `next/headers`, `next/navigation`, and `next/cache`, and by
stubbing `global.fetch`. Component tests use React Testing Library.

## Manual verification checklist (run once per significant change)

This cannot be automated — it requires a live backend and a real browser.

1. Start the backend (`docker compose up` from `backend/`, or `dotnet
   run`), seed an Operator, and use it to create a tenant, a station, and
   log in as that operator to create a `TenantAdmin` user for the tenant
   (or seed one directly via the database for local testing).
2. Start the portal, visit `http://localhost:3000/login`, sign in as the
   `TenantAdmin` user.
3. On **Shifts**, create a shift; confirm it appears in the table.
4. On **Employees**, create an employee, assign the shift from Step 3;
   confirm the employee list shows the shift name, not "—".
5. On **Attendance**, change the date picker to a date with no punches;
   confirm an empty table (not an error). Use the backend's punch
   ingestion endpoint directly (or the desktop agent) to record a punch
   for today, then confirm it appears after reloading `/attendance`.
6. Resize the browser to a phone width (~375px) and repeat steps 3-5;
   confirm the nav wraps sensibly and tables scroll horizontally instead
   of breaking the page layout.
7. Click **Log out**; confirm redirect to `/login` and that navigating
   directly to `/attendance` afterward also redirects to `/login`.
```

- [ ] **Step 3: Commit**

```bash
git add portal
git commit -m "docs: add portal README and manual verification checklist"
```

---

## What Phase 2+ picks up from here

- Late/early/anomaly columns on the attendance view, once the backend's
  `daily_attendance` materialization (Phase 2) exists.
- Station health/sync status view and Recharts-based reporting (Phase 3).
- PWA manifest for "add to home screen" (Phase 4).
