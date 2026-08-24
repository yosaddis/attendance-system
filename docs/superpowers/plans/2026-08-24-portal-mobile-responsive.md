# Portal Mobile Responsiveness (iPhone) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all 5 portal pages usable on an iPhone — a CSS-only responsive pass (no client-side layout JS) replacing wide desktop tables with stacked cards below the `md` breakpoint, plus a bottom tab bar for navigation with proper iPhone home-indicator handling.

**Architecture:** Every page keeps its existing desktop `<table>` (wrapped `hidden md:block`) and adds a sibling `md:hidden` block rendering the same row data as cards. Attendance and Reports share one new `AttendanceRowCard` component (same row shape, same `Badge`); Employees and Shifts get their own small page-specific card markup. Navigation splits into two components sharing one data array: `NavLinks` (desktop, unchanged behavior, now `hidden md:flex`) and a new `MobileTabBar` (fixed to the viewport bottom, `md:hidden`).

**Tech Stack:** Next.js 16 / React 19 server components, Tailwind v4 (existing design tokens in `portal/src/app/globals.css`), Vitest + Testing Library.

## Global Constraints

- No client-side `matchMedia`/JS-driven layout switching — Tailwind `md:` breakpoint (768px) toggles visibility via CSS only; both structures render in the HTML.
- `AttendanceRowCard` props: `row: AttendanceRowResponse`, `shiftName: string`, `showDate: boolean` — used by both Attendance (`showDate={false}`) and Reports (`showDate={true}`).
- Mobile nav labels: only `Employees` gets a shorter `mobileLabel` (`"Staff"`) so all 5 tabs fit at 375px; every other page keeps its existing label on both nav surfaces.
- The bottom tab bar's safe-area handling requires TWO changes together (one without the other does nothing): the root layout's `viewport` export needs `viewportFit: "cover"`, AND the tab bar's own bottom padding must use `env(safe-area-inset-bottom)`.
- Every "add a mobile block" step in this plan gives that block `data-testid="mobile-cards"` so tests can scope into it unambiguously (the desktop table renders the exact same text elsewhere in the DOM, so untargeted `getByText` queries would otherwise match twice and throw).

---

### Task 1: Shared nav data, mobile tab bar, iPhone safe-area setup

**Files:**
- Create: `portal/src/app/(dashboard)/navLinks.ts`
- Modify: `portal/src/app/(dashboard)/NavLinks.tsx`
- Create: `portal/src/app/(dashboard)/MobileTabBar.tsx`
- Test: `portal/src/app/(dashboard)/MobileTabBar.test.tsx`
- Modify: `portal/src/app/(dashboard)/layout.tsx`
- Modify: `portal/src/app/layout.tsx`

**Interfaces:**
- Produces: `NAV_LINKS: { href: string; label: string; mobileLabel?: string }[]` exported from `navLinks.ts` — consumed by both `NavLinks.tsx` and `MobileTabBar.tsx`.

- [ ] **Step 1: Write the failing test**

```tsx
// portal/src/app/(dashboard)/MobileTabBar.test.tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ usePathname: () => "/employees" }));

import { MobileTabBar } from "./MobileTabBar";

describe("MobileTabBar", () => {
  it("uses the shorter mobile label when one is provided", () => {
    render(<MobileTabBar />);

    expect(screen.getByRole("link", { name: "Staff" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Employees" })).not.toBeInTheDocument();
  });

  it("highlights the active tab based on the current pathname", () => {
    render(<MobileTabBar />);

    const activeLink = screen.getByRole("link", { name: "Staff" });
    expect(activeLink.className).toContain("text-accent");

    const inactiveLink = screen.getByRole("link", { name: "Dashboard" });
    expect(inactiveLink.className).toContain("text-ink-soft");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `portal/`): `npm test -- MobileTabBar.test.tsx`
Expected: FAIL — `./MobileTabBar` doesn't exist yet.

- [ ] **Step 3: Create the shared nav data file**

```ts
// portal/src/app/(dashboard)/navLinks.ts
export type NavLink = { href: string; label: string; mobileLabel?: string };

export const NAV_LINKS: NavLink[] = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees", mobileLabel: "Staff" },
  { href: "/shifts", label: "Shifts" },
  { href: "/reports", label: "Reports" },
];
```

- [ ] **Step 4: Update NavLinks.tsx to use the shared data and become desktop-only**

Replace the contents of `portal/src/app/(dashboard)/NavLinks.tsx`:

```tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { NAV_LINKS } from "./navLinks";

export function NavLinks() {
  const pathname = usePathname();

  return (
    <div className="hidden md:flex gap-4">
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

- [ ] **Step 5: Create MobileTabBar.tsx**

```tsx
// portal/src/app/(dashboard)/MobileTabBar.tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { NAV_LINKS } from "./navLinks";

export function MobileTabBar() {
  const pathname = usePathname();

  return (
    <nav className="md:hidden fixed inset-x-0 bottom-0 bg-surface border-t border-border flex">
      {NAV_LINKS.map((link) => {
        const isActive = pathname.startsWith(link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            className={
              "flex-1 text-center text-xs pt-2 pb-[calc(0.5rem+env(safe-area-inset-bottom))] " +
              (isActive ? "text-accent font-medium" : "text-ink-soft")
            }
          >
            {link.mobileLabel ?? link.label}
          </Link>
        );
      })}
    </nav>
  );
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `npm test -- MobileTabBar.test.tsx`
Expected: PASS (2 tests)

- [ ] **Step 7: Wire MobileTabBar and bottom padding into the dashboard layout**

Replace the contents of `portal/src/app/(dashboard)/layout.tsx`:

```tsx
import Image from "next/image";
import { logout } from "./actions";
import { NavLinks } from "./NavLinks";
import { MobileTabBar } from "./MobileTabBar";

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
      <main className="flex-1 bg-surface-muted pb-20 md:pb-0">{children}</main>
      <MobileTabBar />
    </div>
  );
}
```

- [ ] **Step 8: Add iPhone safe-area viewport setting to the root layout**

Replace the contents of `portal/src/app/layout.tsx`:

```tsx
import type { Metadata, Viewport } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Sefed Attendance",
  description: "Attendance and shift management portal",
};

export const viewport: Viewport = {
  viewportFit: "cover",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
```

- [ ] **Step 9: Run the full portal test suite**

Run: `npm test`
Expected: PASS (all tests, no regressions)

- [ ] **Step 10: Commit**

```bash
git add "portal/src/app/(dashboard)/navLinks.ts" "portal/src/app/(dashboard)/NavLinks.tsx" "portal/src/app/(dashboard)/MobileTabBar.tsx" "portal/src/app/(dashboard)/MobileTabBar.test.tsx" "portal/src/app/(dashboard)/layout.tsx" portal/src/app/layout.tsx
git commit -m "feat: add a mobile bottom tab bar with iPhone safe-area support"
```

---

### Task 2: AttendanceRowCard component

**Files:**
- Create: `portal/src/app/(dashboard)/AttendanceRowCard.tsx`
- Test: `portal/src/app/(dashboard)/AttendanceRowCard.test.tsx`

**Interfaces:**
- Consumes: `Badge` from `portal/src/app/(dashboard)/Badge.tsx`, `formatTenantTime` from `@/lib/tenantTime`, `AttendanceRowResponse` from `@/lib/types`.
- Produces: `AttendanceRowCard({ row, shiftName, showDate })` — Tasks 3 and 4 import and render this exact component.

- [ ] **Step 1: Write the failing tests**

```tsx
// portal/src/app/(dashboard)/AttendanceRowCard.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { AttendanceRowCard } from "./AttendanceRowCard";
import type { AttendanceRowResponse } from "@/lib/types";

const baseRow: AttendanceRowResponse = {
  employeeId: "e1",
  employeeName: "On Time Otto",
  date: "2026-01-15",
  shiftId: "s1",
  firstIn: "2026-01-15T05:00:00Z",
  lastOut: "2026-01-15T13:00:00Z",
  workedHours: 8,
  hasShift: true,
  isLate: false,
  lateMinutes: null,
  isMissingCheckout: false,
  hasDoublePunch: false,
};

describe("AttendanceRowCard", () => {
  it("renders the employee name, in/out times, shift, and worked hours", () => {
    render(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("On Time Otto")).toBeInTheDocument();
    expect(screen.getByText(/08:00/)).toBeInTheDocument();
    expect(screen.getByText(/16:00/)).toBeInTheDocument();
    expect(screen.getByText(/Day Shift/)).toBeInTheDocument();
    expect(screen.getByText(/8\.00h/)).toBeInTheDocument();
  });

  it("shows the date only when showDate is true", () => {
    const { rerender } = render(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={false} />);
    expect(screen.queryByText("2026-01-15")).not.toBeInTheDocument();

    rerender(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={true} />);
    expect(screen.getByText("2026-01-15")).toBeInTheDocument();
  });

  it("renders only the badges that are true", () => {
    const lateRow: AttendanceRowResponse = {
      ...baseRow,
      lastOut: null,
      workedHours: null,
      isLate: true,
      lateMinutes: 75,
      isMissingCheckout: true,
    };

    render(<AttendanceRowCard row={lateRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("Late (75m)")).toBeInTheDocument();
    expect(screen.getByText("Missing Checkout")).toBeInTheDocument();
    expect(screen.queryByText("Double Punch")).not.toBeInTheDocument();
  });

  it("renders dashes for null first-in/last-out", () => {
    const absentRow: AttendanceRowResponse = { ...baseRow, firstIn: null, lastOut: null, workedHours: null };

    render(<AttendanceRowCard row={absentRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("— – —")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- AttendanceRowCard.test.tsx`
Expected: FAIL — `./AttendanceRowCard` doesn't exist yet.

- [ ] **Step 3: Write the component**

```tsx
// portal/src/app/(dashboard)/AttendanceRowCard.tsx
import { Badge } from "./Badge";
import type { AttendanceRowResponse } from "@/lib/types";
import { formatTenantTime } from "@/lib/tenantTime";

export function AttendanceRowCard({
  row,
  shiftName,
  showDate,
}: {
  row: AttendanceRowResponse;
  shiftName: string;
  showDate: boolean;
}) {
  const hasAnyFlag = row.isLate || row.isMissingCheckout || row.hasDoublePunch;

  return (
    <div className="p-4">
      <div className="flex items-baseline justify-between gap-2">
        <div className="font-medium text-ink">{row.employeeName}</div>
        {showDate && <div className="text-xs text-ink-soft shrink-0">{row.date}</div>}
      </div>
      <div className="text-sm text-ink mt-1">
        {row.firstIn ? formatTenantTime(row.firstIn) : "—"} – {row.lastOut ? formatTenantTime(row.lastOut) : "—"}
      </div>
      {hasAnyFlag && (
        <div className="mt-1">
          {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
          {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
          {row.hasDoublePunch && <Badge>Double Punch</Badge>}
        </div>
      )}
      <div className="text-xs text-ink-soft mt-1">
        {shiftName}
        {row.workedHours !== null ? ` · ${row.workedHours.toFixed(2)}h` : ""}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- AttendanceRowCard.test.tsx`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add "portal/src/app/(dashboard)/AttendanceRowCard.tsx" "portal/src/app/(dashboard)/AttendanceRowCard.test.tsx"
git commit -m "feat: add AttendanceRowCard for mobile attendance/reports views"
```

---

### Task 3: Wire AttendanceRowCard into the Attendance page

**Files:**
- Modify: `portal/src/app/(dashboard)/attendance/page.tsx`
- Modify: `portal/src/app/(dashboard)/attendance/page.test.tsx`

**Interfaces:**
- Consumes: `AttendanceRowCard` from Task 2.

- [ ] **Step 1: Write the failing test**

Replace the last test in `portal/src/app/(dashboard)/attendance/page.test.tsx` (the one named `"renders worked hours and only the badges that apply"`) with these two, and add `within` to the existing `@testing-library/react` import at the top of the file:

```tsx
import { render, screen, within } from "@testing-library/react";
```

```tsx
  it("renders worked hours and only the badges that apply in the desktop table", async () => {
    vi.mocked(backendFetch).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "On Time Otto",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T05:00:00Z",
        lastOut: "2026-01-15T13:00:00Z",
        workedHours: 8,
        hasShift: true,
        isLate: false,
        lateMinutes: null,
        isMissingCheckout: false,
        hasDoublePunch: false,
      },
      {
        employeeId: "e2",
        employeeName: "Late Larry",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T07:00:00Z",
        lastOut: null,
        workedHours: null,
        hasShift: true,
        isLate: true,
        lateMinutes: 75,
        isMissingCheckout: true,
        hasDoublePunch: false,
      },
    ]);

    render(await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) }));

    const table = screen.getByRole("table");
    expect(within(table).getByText("8.00")).toBeInTheDocument();
    expect(within(table).getByText("Late (75m)")).toBeInTheDocument();
    expect(within(table).getByText(/missing checkout/i)).toBeInTheDocument();
    expect(within(table).queryByText(/double punch/i)).not.toBeInTheDocument();
  });

  it("renders the same rows in the mobile card list", async () => {
    vi.mocked(backendFetch).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "On Time Otto",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T05:00:00Z",
        lastOut: "2026-01-15T13:00:00Z",
        workedHours: 8,
        hasShift: true,
        isLate: false,
        lateMinutes: null,
        isMissingCheckout: false,
        hasDoublePunch: false,
      },
      {
        employeeId: "e2",
        employeeName: "Late Larry",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T07:00:00Z",
        lastOut: null,
        workedHours: null,
        hasShift: true,
        isLate: true,
        lateMinutes: 75,
        isMissingCheckout: true,
        hasDoublePunch: false,
      },
    ]);

    render(await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) }));

    const cards = screen.getByTestId("mobile-cards");
    expect(within(cards).getByText("On Time Otto")).toBeInTheDocument();
    expect(within(cards).getByText("Late Larry")).toBeInTheDocument();
    expect(within(cards).getByText(/8\.00h/)).toBeInTheDocument();
    expect(within(cards).getByText("Late (75m)")).toBeInTheDocument();
    expect(within(cards).getByText(/missing checkout/i)).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- attendance/page.test.tsx`
Expected: FAIL — no element has `data-testid="mobile-cards"` yet, and `within(table)` still passes but the mobile-cards test fails to even find the testid.

- [ ] **Step 3: Update the page**

Replace the contents of `portal/src/app/(dashboard)/attendance/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import { formatTenantTime, todayIsoDateTenant } from "@/lib/tenantTime";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateNav } from "./DateNav";
import { Badge } from "../Badge";
import { AttendanceRowCard } from "../AttendanceRowCard";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

export default async function AttendancePage({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  const { date: requestedDate } = await searchParams;
  const date =
    requestedDate && ISO_DATE_PATTERN.test(requestedDate) && isValidIsoDate(requestedDate)
      ? requestedDate
      : todayIsoDateTenant();
  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/daily?date=${date}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? formatTenantTime(row.firstIn) : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? formatTenantTime(row.lastOut) : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
                  {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
                  {row.hasDoublePunch && <Badge>Double Punch</Badge>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div
        className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg"
        data-testid="mobile-cards"
      >
        {rows.map((row) => (
          <AttendanceRowCard
            key={row.employeeId}
            row={row}
            shiftName={shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}
            showDate={false}
          />
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- attendance/page.test.tsx`
Expected: PASS (7 tests)

- [ ] **Step 5: Commit**

```bash
git add "portal/src/app/(dashboard)/attendance/page.tsx" "portal/src/app/(dashboard)/attendance/page.test.tsx"
git commit -m "feat: show a mobile card list on the attendance page below md"
```

---

### Task 4: Wire AttendanceRowCard into the Reports page and stack its controls

**Files:**
- Modify: `portal/src/app/(dashboard)/reports/page.tsx`
- Modify: `portal/src/app/(dashboard)/reports/page.test.tsx`

**Interfaces:**
- Consumes: `AttendanceRowCard` from Task 2.

- [ ] **Step 1: Write the failing test**

Add `within` to the existing `@testing-library/react` import at the top of `portal/src/app/(dashboard)/reports/page.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
```

Append this test inside the `describe` block:

```tsx
  it("renders row data in both the desktop table and the mobile card list", async () => {
    vi.mocked(backendFetch).mockImplementation((path: string) => {
      if (path.startsWith("/api/shifts")) return Promise.resolve([]);
      return Promise.resolve([
        {
          employeeId: "e1",
          employeeName: "Range Rita",
          date: "2026-01-01",
          shiftId: "s1",
          firstIn: "2026-01-01T05:00:00Z",
          lastOut: "2026-01-01T13:00:00Z",
          workedHours: 8,
          hasShift: true,
          isLate: false,
          lateMinutes: null,
          isMissingCheckout: false,
          hasDoublePunch: false,
        },
      ]);
    });

    render(await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-01" }) }));

    const table = screen.getByRole("table");
    expect(within(table).getByText("Range Rita")).toBeInTheDocument();
    expect(within(table).getByText("8.00")).toBeInTheDocument();

    const cards = screen.getByTestId("mobile-cards");
    expect(within(cards).getByText("Range Rita")).toBeInTheDocument();
    expect(within(cards).getByText("2026-01-01")).toBeInTheDocument();
    expect(within(cards).getByText(/8\.00h/)).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- reports/page.test.tsx`
Expected: FAIL — no element has `data-testid="mobile-cards"` yet.

- [ ] **Step 3: Update the page**

Replace the contents of `portal/src/app/(dashboard)/reports/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import { formatTenantTime, todayIsoDateTenant } from "@/lib/tenantTime";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateRangeNav } from "./DateRangeNav";
import { Badge } from "../Badge";
import { AttendanceRowCard } from "../AttendanceRowCard";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

function defaultRange(): { from: string; to: string } {
  const to = todayIsoDateTenant();
  const from = new Date(new Date(`${to}T00:00:00Z`).getTime() - 6 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 10);
  return { from, to };
}

export default async function ReportsPage({
  searchParams,
}: {
  searchParams: Promise<{ from?: string; to?: string }>;
}) {
  const { from: requestedFrom, to: requestedTo } = await searchParams;
  const fallback = defaultRange();
  const from =
    requestedFrom && ISO_DATE_PATTERN.test(requestedFrom) && isValidIsoDate(requestedFrom)
      ? requestedFrom
      : fallback.from;
  const to =
    requestedTo && ISO_DATE_PATTERN.test(requestedTo) && isValidIsoDate(requestedTo)
      ? requestedTo
      : fallback.to;

  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/report?from=${from}&to=${to}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Reports</h1>
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
        <DateRangeNav from={from} to={to} />
        <a
          href={`/reports/export?from=${from}&to=${to}`}
          className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent w-full sm:w-auto text-center"
        >
          Export CSV
        </a>
      </div>
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Date</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.employeeId}-${row.date}`} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{row.date}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? formatTenantTime(row.firstIn) : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? formatTenantTime(row.lastOut) : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
                  {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
                  {row.hasDoublePunch && <Badge>Double Punch</Badge>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div
        className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg"
        data-testid="mobile-cards"
      >
        {rows.map((row) => (
          <AttendanceRowCard
            key={`${row.employeeId}-${row.date}`}
            row={row}
            shiftName={shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}
            showDate={true}
          />
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm test -- reports/page.test.tsx`
Expected: PASS (4 tests)

- [ ] **Step 5: Run the full portal test suite**

Run: `npm test`
Expected: PASS (all tests)

- [ ] **Step 6: Commit**

```bash
git add "portal/src/app/(dashboard)/reports/page.tsx" "portal/src/app/(dashboard)/reports/page.test.tsx"
git commit -m "feat: show a mobile card list and stacked controls on the reports page"
```

---

### Task 5: Employees and Shifts mobile card views

**Files:**
- Modify: `portal/src/app/(dashboard)/employees/page.tsx`
- Modify: `portal/src/app/(dashboard)/shifts/page.tsx`

**Interfaces:** none — this task doesn't touch or depend on `AttendanceRowCard`. Neither page has an existing `page.test.tsx` (only their form components are unit-tested); this task doesn't add one, matching that existing convention — coverage here comes from Task 6's manual pass.

- [ ] **Step 1: Update the Employees page**

Replace the contents of `portal/src/app/(dashboard)/employees/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { EmployeeForm } from "./EmployeeForm";
import { deleteEmployee } from "./actions";

export default async function EmployeesPage() {
  const token = await getToken();
  const [employees, shifts]: [EmployeeResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch("/api/employees", { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Employees</h1>
      <EmployeeForm shifts={shifts} />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
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
      <div className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg">
        {employees.map((employee) => (
          <div key={employee.id} className="p-4 flex items-center justify-between gap-3">
            <div>
              <div className="font-medium text-ink">{employee.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {employee.employeeCode} · {shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}
              </div>
            </div>
            <form action={deleteEmployee.bind(null, employee.id)}>
              <button type="submit" className="text-danger text-sm shrink-0">
                Delete
              </button>
            </form>
          </div>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Update the Shifts page**

Replace the contents of `portal/src/app/(dashboard)/shifts/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { ShiftResponse } from "@/lib/types";
import { ShiftForm } from "./ShiftForm";
import { deleteShift } from "./actions";

export default async function ShiftsPage() {
  const shifts: ShiftResponse[] = await backendFetch("/api/shifts", { token: await getToken() });

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Shifts</h1>
      <ShiftForm />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
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
      <div className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg">
        {shifts.map((shift) => (
          <div key={shift.id} className="p-4 flex items-center justify-between gap-3">
            <div>
              <div className="font-medium text-ink">{shift.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {shift.startTime}–{shift.endTime} · {shift.graceMinutes}min grace · {shift.punchMode}
              </div>
            </div>
            <form action={deleteShift.bind(null, shift.id)}>
              <button type="submit" className="text-danger text-sm shrink-0">
                Delete
              </button>
            </form>
          </div>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Run the full portal test suite**

Run: `npm test`
Expected: PASS (all tests — this task added no new tests, so the count is unchanged from Task 4)

- [ ] **Step 4: Commit**

```bash
git add "portal/src/app/(dashboard)/employees/page.tsx" "portal/src/app/(dashboard)/shifts/page.tsx"
git commit -m "feat: show mobile card lists on the employees and shifts pages"
```

---

### Task 6: Manual verification across all 5 pages at iPhone width

**Files:** none — this task is verification only, no code changes.

- [ ] **Step 1: Build and start the portal**

Run (from `portal/`): `npm run build && npm run start`
(Rebuild is required — `next start` serves whatever was last built; see the project's own recent history of this exact mistake.)

- [ ] **Step 2: Resize the browser to the iPhone preset (375×812) and log in**

Navigate to the portal's login page, sign in with a TenantAdmin account.

- [ ] **Step 3: Click through all 5 pages via the bottom tab bar**

For each of Dashboard, Attendance, Employees, Shifts, Reports:
- Confirm the bottom tab bar is visible, all 5 labels fit on one row without wrapping or overflowing, and the current page's tab is visually distinguished (accent color) from the other four.
- Confirm no horizontal scrolling is needed anywhere on the page.
- Confirm the desktop `<table>` is NOT visible (CSS-hidden) and the card list IS visible, with the same data as it would show in the desktop table (spot-check against the same page at a wide viewport).
- On Reports specifically: confirm the From/To date inputs and the Export CSV button stack vertically and are each comfortably tappable (not tiny), and Export CSV still downloads a CSV when tapped.

- [ ] **Step 4: Confirm content isn't hidden behind the bottom tab bar**

Scroll to the bottom of the longest page (Reports, with a wide date range) and confirm the last card is fully visible above the tab bar, not clipped underneath it.

- [ ] **Step 5: Confirm desktop is unaffected**

Resize the browser back to the desktop preset (1280×800) and click through all 5 pages again — confirm the original desktop tables and top nav render exactly as before this plan (no card lists visible, no bottom tab bar visible).

- [ ] **Step 6: Note the safe-area caveat**

The `env(safe-area-inset-bottom)` padding on the bottom tab bar can only be fully confirmed on a real iPhone or Safari's device simulator — a desktop browser's mobile viewport emulation reports `0` for that value, so the tab bar will look fine in this pass regardless. Flag this to the user as unverified-on-real-hardware rather than claiming it as confirmed.
