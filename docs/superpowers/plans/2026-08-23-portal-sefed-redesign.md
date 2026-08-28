# Portal Visual Redesign (Sefed Brand) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the portal (login + 3 dashboard pages + nav shell) using the real Sefed Systems brand kit, and rename the product to "Sefed Attendance" in every user-visible string.

**Architecture:** Define brand colors/fonts once as Tailwind v4 `@theme` tokens in `globals.css`, self-host the brand's actual font files via `next/font/local`, then apply the tokens page-by-page. Visual-only change — no routing, data-fetching, or validation logic changes. Existing tests use semantic queries (`getByLabelText`, `getByRole`) and mock the backend, not CSS classes, so they must all keep passing unchanged.

**Tech Stack:** Next.js 16.3.1, React 19, Tailwind CSS v4, Vitest + Testing Library (already installed, no new dependencies).

## Global Constraints

- Base theme is light (`--color-surface` white/off-white); dark teal (`--color-ink`) is reserved for the nav bar only, never a page background.
- Exact token values (copy verbatim, do not approximate):
  - `--color-surface: #FFFFFF`
  - `--color-surface-muted: #F4F6F6`
  - `--color-ink: #174249`
  - `--color-ink-soft: #3C6066`
  - `--color-border: #DADADA`
  - `--color-accent: #E2B260`
  - `--color-accent-hover: #D0822F`
  - `--color-accent-ink: #174249`
  - `--color-danger: #DC2626`
  - `--color-danger-bg: #FEF2F2`
- Product display name is "Sefed Attendance" everywhere a user sees it (page titles, headings) — never in file paths, npm package name, repo name, or code identifiers.
- No new npm dependencies. No component library. No dark-mode toggle.
- Every existing test in `portal/src` (`npm test`) must still pass unmodified — they assert on labels/roles/behavior, not styling, so a correct visual-only change never requires editing a test file. If a task's change would require editing an existing test, stop and reconsider the change (likely a semantics change that's out of scope).

---

### Task 1: Design tokens, fonts, and logo assets

**Files:**
- Modify: `portal/src/app/globals.css`
- Modify: `portal/src/app/layout.tsx`
- Create: `portal/public/fonts/Figtree-Regular.ttf`, `Figtree-Medium.ttf`, `Figtree-SemiBold.ttf`, `Figtree-Bold.ttf` (copied from `C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Primary\Figtree\static\`)
- Create: `portal/public/fonts/ChakraPetch-Regular.ttf`, `ChakraPetch-SemiBold.ttf` (copied from `C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Secondary\Chakra_Petch\`)
- Create: `portal/public/sefed-icon.png` (copied from `C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\Logo\Icon\Icon-01.png`)

**Interfaces:**
- Produces: Tailwind theme tokens `--color-surface`, `--color-surface-muted`, `--color-ink`, `--color-ink-soft`, `--color-border`, `--color-accent`, `--color-accent-hover`, `--color-accent-ink`, `--color-danger`, `--color-danger-bg`, `--font-sans`, `--font-display` — every later task consumes these by name via Tailwind utility classes (e.g. `bg-accent`, `text-ink`, `font-display`) or CSS `var(--color-...)`. The `/sefed-icon.png` and `/fonts/*` public paths are consumed by Task 2 (nav) and Task 3 (login).

- [ ] **Step 1: Copy the font files and icon asset**

```bash
mkdir -p "portal/public/fonts"
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Primary\Figtree\static\Figtree-Regular.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Primary\Figtree\static\Figtree-Medium.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Primary\Figtree\static\Figtree-SemiBold.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Primary\Figtree\static\Figtree-Bold.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Secondary\Chakra_Petch\ChakraPetch-Regular.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\Secondary\Chakra_Petch\ChakraPetch-SemiBold.ttf" portal/public/fonts/
cp "C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\Logo\Icon\Icon-01.png" portal/public/sefed-icon.png
```

Expected: 6 `.ttf` files exist under `portal/public/fonts/`, and `portal/public/sefed-icon.png` exists. Open `sefed-icon.png` (or the equivalent brand guideline PDF page showing the icon) to confirm it's the small circular gold+silver "S" mark, not a different icon variant from the same folder tree (that folder has several historical iterations — `Icon-01.png` alongside `IconVariants/Icon-02.png` through `Icon-06.png` — the guideline PDF's own pages 9, 12, 17, 22 all show the SAME final mark, so cross-check against any of those before trusting the filename alone).

- [ ] **Step 2: Add the design tokens to `globals.css`**

```css
/* portal/src/app/globals.css — full file */
@import "tailwindcss";

@font-face {
  font-family: "Figtree";
  src: url("/fonts/Figtree-Regular.ttf") format("truetype");
  font-weight: 400;
  font-display: swap;
}

@font-face {
  font-family: "Figtree";
  src: url("/fonts/Figtree-Medium.ttf") format("truetype");
  font-weight: 500;
  font-display: swap;
}

@font-face {
  font-family: "Figtree";
  src: url("/fonts/Figtree-SemiBold.ttf") format("truetype");
  font-weight: 600;
  font-display: swap;
}

@font-face {
  font-family: "Figtree";
  src: url("/fonts/Figtree-Bold.ttf") format("truetype");
  font-weight: 700;
  font-display: swap;
}

@font-face {
  font-family: "Chakra Petch";
  src: url("/fonts/ChakraPetch-Regular.ttf") format("truetype");
  font-weight: 400;
  font-display: swap;
}

@font-face {
  font-family: "Chakra Petch";
  src: url("/fonts/ChakraPetch-SemiBold.ttf") format("truetype");
  font-weight: 600;
  font-display: swap;
}

:root {
  --color-surface: #ffffff;
  --color-surface-muted: #f4f6f6;
  --color-ink: #174249;
  --color-ink-soft: #3c6066;
  --color-border: #dadada;
  --color-accent: #e2b260;
  --color-accent-hover: #d0822f;
  --color-accent-ink: #174249;
  --color-danger: #dc2626;
  --color-danger-bg: #fef2f2;
}

@theme inline {
  --color-surface: var(--color-surface);
  --color-surface-muted: var(--color-surface-muted);
  --color-ink: var(--color-ink);
  --color-ink-soft: var(--color-ink-soft);
  --color-border: var(--color-border);
  --color-accent: var(--color-accent);
  --color-accent-hover: var(--color-accent-hover);
  --color-accent-ink: var(--color-accent-ink);
  --color-danger: var(--color-danger);
  --color-danger-bg: var(--color-danger-bg);
  --font-sans: "Figtree", ui-sans-serif, system-ui, sans-serif;
  --font-display: "Chakra Petch", ui-sans-serif, system-ui, sans-serif;
}

body {
  background: var(--color-surface-muted);
  color: var(--color-ink);
  font-family: var(--font-sans);
}
```

This makes `bg-surface`, `bg-surface-muted`, `text-ink`, `text-ink-soft`, `border-border` (Tailwind's default border-color utility already reads `--color-border`, so plain `border` classes pick this up automatically), `bg-accent`, `hover:bg-accent-hover`, `text-accent-ink`, `text-danger`, `bg-danger-bg`, and `font-display` all available as Tailwind utility classes in every subsequent task.

- [ ] **Step 3: Rename the product and remove the old Google Fonts, in the root layout**

```tsx
// portal/src/app/layout.tsx — full file
import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Sefed Attendance",
  description: "Attendance and shift management portal",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
```

This drops `Geist`/`Geist_Mono` from `next/font/google` entirely — the `@font-face` rules in `globals.css` (Step 2) now supply every typeface the app uses, and nothing else in the codebase references `--font-geist-sans`/`--font-geist-mono` (confirm with the grep in Step 4 below).

- [ ] **Step 4: Confirm no other file references the old fonts or product name**

Run: `grep -rn "Geist\|ZAK Attendance" portal/src`
Expected: no matches. If any appear outside files this task already touched, note them for Task 2 (the nav shell is the other place "ZAK Attendance" could plausibly appear, though it currently doesn't per the codebase as read during planning).

- [ ] **Step 5: Build and run the existing test suite**

Run: `cd portal && npm run build`
Expected: build succeeds (this also validates the `@font-face` paths resolve and `globals.css` is syntactically valid).

Run: `npm test`
Expected: all existing tests still pass (this task touches no component logic).

- [ ] **Step 6: Commit**

```bash
git add portal/public/fonts portal/public/sefed-icon.png portal/src/app/globals.css portal/src/app/layout.tsx
git commit -m "feat: add Sefed brand design tokens, fonts, and logo asset"
```

---

### Task 2: Nav shell — logo, product rename, active-link styling

**Files:**
- Modify: `portal/src/app/(dashboard)/layout.tsx`
- Create: `portal/src/app/(dashboard)/NavLinks.tsx`

**Interfaces:**
- Consumes: Task 1's tokens (`bg-ink`, `text-accent`, `font-display`, `/sefed-icon.png`).
- Produces: `NavLinks` (a Client Component, default export), consumed only by `DashboardLayout` in this task. `DashboardLayout` remains the default export other files already import implicitly via Next.js routing conventions (no explicit import elsewhere to update).

Next.js 16 confirms (`node_modules/next/dist/docs/01-app/03-api-reference/04-functions/use-pathname.md`): "Reading the current URL from a Server Component is not supported" — `usePathname()` requires `"use client"`. `DashboardLayout` itself stays a Server Component (it doesn't need any client state beyond the active-link check), so only the three nav links are pulled into their own small Client Component — this is the documented, recommended pattern (push `"use client"` down to the smallest piece that needs it, not the whole layout).

- [ ] **Step 1: Create the client-side nav links component**

```tsx
// portal/src/app/(dashboard)/NavLinks.tsx — full file
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const NAV_LINKS = [
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees" },
  { href: "/shifts", label: "Shifts" },
];

export function NavLinks() {
  const pathname = usePathname();

  return (
    <div className="flex gap-4">
      {NAV_LINKS.map((link) => {
        const isActive = pathname.startsWith(link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            className={
              isActive
                ? "text-accent border-b-2 border-accent pb-1"
                : "text-surface/80 hover:text-accent pb-1 border-b-2 border-transparent"
            }
          >
            {link.label}
          </Link>
        );
      })}
    </div>
  );
}
```

The inactive-link's `border-b-2 border-transparent` (matching the active link's border width) prevents a 2px layout shift when a link becomes active — both states reserve the same space.

- [ ] **Step 2: Rewrite the nav shell to use it**

```tsx
// portal/src/app/(dashboard)/layout.tsx — full file
import Image from "next/image";
import { logout } from "./actions";
import { NavLinks } from "./NavLinks";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col">
      <nav className="flex flex-wrap items-center gap-6 bg-ink px-6 py-3">
        <div className="flex items-center gap-2">
          <Image src="/sefed-icon.png" alt="" width={28} height={28} />
          <div className="leading-none">
            <div className="text-surface font-medium text-sm">Sefed</div>
            <div className="font-display text-accent text-[10px] tracking-[0.2em]">ATTENDANCE</div>
          </div>
        </div>
        <NavLinks />
        <form action={logout} className="ml-auto">
          <button type="submit" className="text-sm text-surface/70 hover:text-accent">
            Log out
          </button>
        </form>
      </nav>
      <main className="flex-1 bg-surface-muted">{children}</main>
    </div>
  );
}
```

`DashboardLayout` no longer needs to be `async` — it no longer awaits anything itself (the previous version didn't either; this is unchanged, noted only because Step 1 of the original draft incorrectly introduced an `await headers()` call that this corrected version does not have).

- [ ] **Step 3: Build and visually check**

Run: `cd portal && npm run dev` (or reuse an already-running dev server) and open `/attendance` (after logging in) in a browser. Confirm: dark teal nav bar, icon + "Sefed / ATTENDANCE" lockup on the left, three links, gold active-state indicator on whichever page is open (or absent, per Step 1's fallback note), "Log out" on the right.

Run: `npm test`
Expected: all existing tests still pass (this task touches no page under test — `page.test.tsx` files mock the backend and test page components directly, not the shared layout).

- [ ] **Step 4: Commit**

```bash
git add "portal/src/app/(dashboard)/layout.tsx" "portal/src/app/(dashboard)/NavLinks.tsx"
git commit -m "feat: restyle the portal nav shell with the Sefed brand"
```

---

### Task 3: Login page

**Files:**
- Modify: `portal/src/app/login/page.tsx`

**Interfaces:**
- Consumes: Task 1's tokens; the existing `login` server action from `./actions` (unchanged, already imported).

- [ ] **Step 1: Rewrite the login page**

```tsx
// portal/src/app/login/page.tsx — full file
import Image from "next/image";
import { login } from "./actions";

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  const errorMessage =
    error === "unreachable"
      ? "Unable to reach the server. Please try again."
      : error === "role"
        ? "This account doesn't have access to the portal."
        : error
          ? "Invalid email or password."
          : null;

  return (
    <div className="min-h-screen flex items-center justify-center p-4 bg-surface-muted">
      <form
        action={login}
        className="w-full max-w-sm space-y-4 bg-surface border border-border rounded-lg shadow-md p-8"
      >
        <div className="flex flex-col items-center gap-3 mb-2">
          <Image src="/sefed-icon.png" alt="" width={48} height={48} />
          <h1 className="text-ink text-center">
            Sign in to <span className="font-display">Sefed Attendance</span>
          </h1>
        </div>
        {errorMessage && (
          <p className="bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
            {errorMessage}
          </p>
        )}
        <label className="flex flex-col text-sm gap-1 text-ink-soft">
          Email
          <input
            type="email"
            name="email"
            required
            className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
          />
        </label>
        <label className="flex flex-col text-sm gap-1 text-ink-soft">
          Password
          <input
            type="password"
            name="password"
            required
            className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
          />
        </label>
        <button
          type="submit"
          className="w-full bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-3 py-2 transition-colors"
        >
          Sign in
        </button>
      </form>
    </div>
  );
}
```

Note `border-danger/20` — Tailwind's opacity modifier syntax works on any color token including custom ones, giving a subtle border instead of a full-strength red line around the error callout.

- [ ] **Step 2: Confirm the existing login test still passes**

Read `portal/src/app/login/actions.test.ts` first — it tests the `login` action's redirect/cookie logic, not this page component, so it should be entirely unaffected. Run: `npm test -- login`
Expected: pass, unchanged.

- [ ] **Step 3: Visually check**

`npm run dev`, open `/login`. Confirm: light background, white card with border/shadow, icon above the heading, gold-outlined focus states on the inputs (click into a field to check), gold submit button with dark teal text, and (if you can trigger an error, e.g. wrong password) a light-red callout box rather than bare red text.

- [ ] **Step 4: Commit**

```bash
git add portal/src/app/login/page.tsx
git commit -m "feat: restyle the login page with the Sefed brand"
```

---

### Task 4: Attendance page

**Files:**
- Modify: `portal/src/app/(dashboard)/attendance/page.tsx`
- Modify: `portal/src/app/(dashboard)/attendance/DateNav.tsx`

**Interfaces:**
- Consumes: Task 1's tokens. `AttendancePage`'s props/logic (the date-fallback behavior `page.test.tsx` exercises) are UNCHANGED — only the returned JSX's class names change.

- [ ] **Step 1: Restyle the page**

```tsx
// portal/src/app/(dashboard)/attendance/page.tsx — replace only the returned JSX; keep every import, the ISO_DATE_PATTERN constant, todayIsoDate, isValidIsoDate, and the AttendancePage function signature/body exactly as they are today up through the `const rows = ...` line
  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
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
// portal/src/app/(dashboard)/attendance/DateNav.tsx — full file
"use client";

import { useRouter } from "next/navigation";

export function DateNav({ date }: { date: string }) {
  const router = useRouter();

  return (
    <input
      type="date"
      defaultValue={date}
      onChange={(e) => router.push(`/attendance?date=${e.target.value}`)}
      className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
    />
  );
}
```

- [ ] **Step 2: Run the existing page test**

Run: `npm test -- attendance`
Expected: all 5 existing tests in `page.test.tsx` still pass unchanged (they call `AttendancePage(...)` directly and assert on `backendFetch` call arguments, never rendering to the DOM, so styling changes cannot affect them).

- [ ] **Step 3: Visually check**

`npm run dev`, open `/attendance`. Confirm: card-wrapped table, muted header row, hover-highlight on data rows, styled date input matching the login input treatment.

- [ ] **Step 4: Commit**

```bash
git add "portal/src/app/(dashboard)/attendance"
git commit -m "feat: restyle the attendance page with the Sefed brand"
```

---

### Task 5: Employees page and form

**Files:**
- Modify: `portal/src/app/(dashboard)/employees/page.tsx`
- Modify: `portal/src/app/(dashboard)/employees/EmployeeForm.tsx`

**Interfaces:**
- Consumes: Task 1's tokens. `EmployeeForm`'s props (`shifts: ShiftResponse[]`) and the `<label>`/`<input>` structure `EmployeeForm.test.tsx` queries by (`getByLabelText(/employee code/i)`, `getByLabelText(/^name$/i)`, `getByRole("option", { name: "Day Shift" })`) are UNCHANGED — label text and `required` attributes must stay exactly as they are.

- [ ] **Step 1: Restyle `EmployeeForm.tsx`**

```tsx
// portal/src/app/(dashboard)/employees/EmployeeForm.tsx — full file
"use client";

import { useActionState } from "react";
import type { ShiftResponse } from "@/lib/types";
import { createEmployee, type CreateEmployeeState } from "./actions";

export function EmployeeForm({ shifts }: { shifts: ShiftResponse[] }) {
  const [state, formAction, isPending] = useActionState<CreateEmployeeState, FormData>(
    createEmployee,
    null,
  );

  return (
    <form
      action={formAction}
      className="grid grid-cols-2 gap-3 sm:grid-cols-4 items-end bg-surface border border-border rounded-lg p-4"
    >
      {state?.error && (
        <p role="alert" className="col-span-full bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
          {state.error}
        </p>
      )}
      <label htmlFor="employeeCode" className="flex flex-col text-sm gap-1 text-ink-soft">
        Employee code
        <input
          id="employeeCode"
          name="employeeCode"
          required
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label htmlFor="name" className="flex flex-col text-sm gap-1 text-ink-soft">
        Name
        <input
          id="name"
          name="name"
          required
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label htmlFor="shiftId" className="flex flex-col text-sm gap-1 text-ink-soft">
        Shift
        <select
          id="shiftId"
          name="shiftId"
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        >
          <option value="">— None —</option>
          {shifts.map((shift) => (
            <option key={shift.id} value={shift.id}>
              {shift.name}
            </option>
          ))}
        </select>
      </label>
      <button
        type="submit"
        disabled={isPending}
        className="bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-4 py-2 transition-colors disabled:opacity-60"
      >
        Add employee
      </button>
    </form>
  );
}
```

- [ ] **Step 2: Restyle `page.tsx`**

```tsx
// portal/src/app/(dashboard)/employees/page.tsx — replace only the returned JSX; keep every import and the function body up through `Promise.all([...])` exactly as they are today
  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Employees</h1>
      <EmployeeForm shifts={shifts} />
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Code</th>
              <th className="py-3 px-4 font-medium">Name</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4"></th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.id} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{employee.employeeCode}</td>
                <td className="py-3 px-4">{employee.name}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">
                  <form action={deleteEmployee.bind(null, employee.id)}>
                    <button type="submit" className="text-danger hover:underline text-sm">
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

- [ ] **Step 3: Run the existing tests**

Run: `npm test -- employees`
Expected: `EmployeeForm.test.tsx`'s 2 tests and `actions.test.ts`'s tests all pass unchanged.

- [ ] **Step 4: Visually check**

`npm run dev`, open `/employees`. Confirm: card-wrapped form with gold submit button, card-wrapped table matching the Attendance page's treatment, red-but-subtle delete links.

- [ ] **Step 5: Commit**

```bash
git add "portal/src/app/(dashboard)/employees"
git commit -m "feat: restyle the employees page with the Sefed brand"
```

---

### Task 6: Shifts page and form

**Files:**
- Modify: `portal/src/app/(dashboard)/shifts/page.tsx`
- Modify: `portal/src/app/(dashboard)/shifts/ShiftForm.tsx`

**Interfaces:**
- Consumes: Task 1's tokens. `ShiftForm` has no dedicated test file today (only `shifts/actions.test.ts`, which tests the server action, not this component) — no label-text/structure constraint beyond keeping the existing field names (`name`, `startTime`, `endTime`, `graceMinutes`, `punchMode`) since `actions.ts` reads them by those names from `FormData`.

- [ ] **Step 1: Restyle `ShiftForm.tsx`**

```tsx
// portal/src/app/(dashboard)/shifts/ShiftForm.tsx — full file
"use client";

import { useActionState } from "react";
import { createShift, type CreateShiftState } from "./actions";

export function ShiftForm() {
  const [state, formAction, isPending] = useActionState<CreateShiftState, FormData>(
    createShift,
    null,
  );

  const inputClass =
    "border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent";

  return (
    <form
      action={formAction}
      className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-6 items-end bg-surface border border-border rounded-lg p-4"
    >
      {state?.error && (
        <p role="alert" className="col-span-full bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
          {state.error}
        </p>
      )}
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Name
        <input name="name" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Start
        <input type="time" step={1} name="startTime" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        End
        <input type="time" step={1} name="endTime" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Grace (min)
        <input type="number" name="graceMinutes" defaultValue={0} className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Punch mode
        <select name="punchMode" className={inputClass}>
          <option value="TwoPunch">2-punch (IN/OUT)</option>
          <option value="FourPunch">4-punch (+ breaks)</option>
        </select>
      </label>
      <button
        type="submit"
        disabled={isPending}
        className="bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-4 py-2 transition-colors disabled:opacity-60"
      >
        Add shift
      </button>
    </form>
  );
}
```

- [ ] **Step 2: Restyle `page.tsx`**

```tsx
// portal/src/app/(dashboard)/shifts/page.tsx — replace only the returned JSX; keep every import and the function body up through `const shifts = ...` exactly as they are today
  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Shifts</h1>
      <ShiftForm />
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Name</th>
              <th className="py-3 px-4 font-medium">Start</th>
              <th className="py-3 px-4 font-medium">End</th>
              <th className="py-3 px-4 font-medium">Grace (min)</th>
              <th className="py-3 px-4 font-medium">Mode</th>
              <th className="py-3 px-4"></th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((shift) => (
              <tr key={shift.id} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{shift.name}</td>
                <td className="py-3 px-4">{shift.startTime}</td>
                <td className="py-3 px-4">{shift.endTime}</td>
                <td className="py-3 px-4">{shift.graceMinutes}</td>
                <td className="py-3 px-4">{shift.punchMode}</td>
                <td className="py-3 px-4">
                  <form action={deleteShift.bind(null, shift.id)}>
                    <button type="submit" className="text-danger hover:underline text-sm">
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

- [ ] **Step 3: Run the existing tests, then the full suite one more time**

Run: `npm test -- shifts`
Expected: `actions.test.ts`'s tests pass unchanged (no dedicated `ShiftForm`/page test exists to run here).

Run: `npm test`
Expected: the entire suite (all 7 test files) passes.

Run: `npm run build`
Expected: succeeds.

- [ ] **Step 4: Final visual pass**

`npm run dev`. Click through Login → Attendance → Employees → Shifts → Log out, confirming a consistent look across every page: dark teal nav with the Sefed lockup, light surfaces elsewhere, gold buttons/focus rings/active-link indicator, card-wrapped tables and forms, red-but-subtle error/delete treatments.

- [ ] **Step 5: Commit**

```bash
git add "portal/src/app/(dashboard)/shifts"
git commit -m "feat: restyle the shifts page with the Sefed brand"
```
