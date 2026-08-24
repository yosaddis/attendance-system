# Portal Mobile Responsiveness (iPhone) — Design

## Purpose

The portal is going to be used on iPhones specifically. Today every page is
built desktop-first: wide multi-column `<table>`s that require horizontal
scrolling on a 375px-wide screen, and a top nav with 5 text links plus a
logout button crammed into one row. This redesigns all 5 pages (Dashboard,
Attendance, Employees, Shifts, Reports) and the navigation to work well on
an iPhone, with Reports getting a deliberately simplified ("reduced") mobile
view since its table is the widest (7 columns).

Confirmed directly by resizing a browser to 375×812 and inspecting the
Reports page: the accessibility tree shows the full 7-column table
(Employee, Date, Shift, First In, Last Out, Worked Hours, Flags) rendered
as-is inside `overflow-x-auto` — on a real iPhone this means horizontal
scrolling to read anything past "Shift."

## Scope

In scope: Dashboard, Attendance, Employees, Shifts, Reports pages; the top
nav / new bottom tab bar; iPhone-specific safe-area handling for the home
indicator.

Out of scope (already fine, no changes): the Login page (already a single
centered card, no table) and the error boundary page.

This is one cohesive design (a responsive pass across the existing portal),
not multiple independent subsystems, so it stays a single spec/plan.

## Approach

Everything stays a server component — no client-side `matchMedia` or
JavaScript-driven layout switching. Each page renders its existing desktop
`<table>` wrapped in `hidden md:block`, plus a new sibling block wrapped in
`md:hidden` that renders the same row data as a stacked list of cards. Both
structures ship in the HTML; Tailwind's `md:` breakpoint (768px) toggles
which one is visible via CSS. This costs a slightly larger HTML payload for
a handful of rows on a tenant-admin tool — a fine trade for keeping every
page a plain server component with no hydration cost.

Two pages (Attendance, Reports) share the exact same row shape
(`AttendanceRowResponse`) and already share the `Badge` component, so their
mobile card rendering is factored into one new shared component,
`AttendanceRowCard`, rather than duplicated. Employees and Shifts keep
their own page-specific card markup — their data shapes are simpler and
different enough that a shared component would be forcing an abstraction
that doesn't fit both.

## Components

### `AttendanceRowCard` (new, shared by Attendance + Reports)

`portal/src/app/(dashboard)/AttendanceRowCard.tsx`. Props: `row:
AttendanceRowResponse`, `shiftName: string`, `showDate: boolean`. Renders,
per card: employee name and First In – Last Out prominent (normal
text/font-medium); any true flags (Late with minutes, Missing Checkout,
Double Punch) as `Badge`s directly under that; shift name and worked hours
smaller/secondary (`text-xs text-ink-soft`) at the bottom. `showDate` is
`true` on Reports (its rows span multiple days) and `false` on Attendance
(implicitly "today").

### Attendance / Reports pages

Each adds a `md:hidden` block right beside its existing (now `hidden
md:block`) table, mapping the same `rows` array through `AttendanceRowCard`
— no new data fetching, no new props beyond what each page already
computes (the `shifts.find(...)` lookup already used to build the desktop
table's Shift column).

### Employees / Shifts pages

Each adds its own small `md:hidden` card list beside its (now `hidden
md:block`) table: Employees shows Name prominent with Code + Shift name
secondary and the Delete action; Shifts shows Name prominent with
Start–End, grace minutes, and punch mode secondary, plus Delete. No shared
component between these two — reusing `AttendanceRowCard` here would mean
threading unrelated fields (dates, punches) through props that don't apply.

### Dashboard

Its 4 KPI cards already use `grid-cols-2 md:grid-cols-4`, which already
reads fine at 375px (2×2 grid). No structural change needed here beyond
whatever the nav change requires.

### Navigation: bottom tab bar on mobile, existing top nav on desktop

The nav items (href + label) move into a small shared data file,
`portal/src/app/(dashboard)/navLinks.ts` (plain array, no `"use client"`
needed), each entry optionally carrying a shorter `mobileLabel` (only
`Employees` needs one — `"Staff"` — to keep 5 items comfortably spaced at
375px). Both nav components import this one array so the link list and
active-path logic aren't duplicated.

- `NavLinks.tsx` (existing, edited): unchanged behavior, just wraps its
  root in `hidden md:flex` instead of `flex` — it becomes desktop-only.
- `MobileTabBar.tsx` (new): a `md:hidden` bar fixed to the bottom of the
  viewport (`fixed inset-x-0 bottom-0`), `bg-surface` with a `border-t
  border-border` separator, the 5 items evenly spaced
  (`mobileLabel ?? label`), active item in `text-accent`, inactive in
  `text-ink-soft`. Rendered as a sibling of `<main>` in the dashboard
  layout, not nested inside the top `<nav>`.
- `(dashboard)/layout.tsx` (edited): adds `<MobileTabBar />` after
  `<main>`, and adds bottom padding to `<main>` (`pb-20 md:pb-0`) so page
  content doesn't sit behind the fixed bar.
- Top bar on mobile keeps just the logo/wordmark and the logout button
  (both already there; only the link list moves out).

**iPhone-specific detail:** the fixed bottom bar must account for the home
indicator gesture area on iPhones with no physical home button, or it sits
underneath/behind it. This needs two changes together — one without the
other does nothing:
1. `portal/src/app/layout.tsx` (root layout, outside the dashboard group)
   exports `viewport: Viewport = { viewportFit: "cover" }` — without this,
   `env(safe-area-inset-bottom)` always resolves to `0` on iPhone Safari.
2. `MobileTabBar`'s own bottom padding uses
   `pb-[calc(0.5rem+env(safe-area-inset-bottom))]` instead of a plain
   `pb-2`, so the tappable area clears the home indicator.

### Reports' date-range controls

`reports/page.tsx`'s wrapper around `<DateRangeNav>` and the "Export CSV"
link changes from `flex flex-wrap items-end justify-between gap-4` to `flex
flex-col sm:flex-row sm:items-end sm:justify-between gap-4`, so on mobile
the date-range fields stack above a full-width "Export CSV" button
(`w-full sm:w-auto text-center` added to that link) instead of both being
squeezed into one cramped row. `DateRangeNav`'s own internal
`flex-wrap` already lets its two date inputs + Apply button wrap
reasonably at narrow widths — left as-is.

## Testing

- Component tests for `AttendanceRowCard`: renders employee name and
  In/Out correctly, renders only the badges that are true (mirroring the
  existing `attendance/page.test.tsx` coverage for the desktop table),
  renders "—" for null first-in/last-out, and correctly shows/hides the
  Date field per `showDate`.
- Existing `attendance/page.test.tsx` and `reports/page.test.tsx` gain
  assertions that the new mobile card block renders the same row data
  (spot-checking a couple of fields) alongside the existing desktop-table
  assertions — both blocks exist in the DOM simultaneously (CSS-hidden, not
  removed), so both are testable without any viewport simulation in Vitest.
- Manual verification: resize the browser to the iPhone preset (375×812)
  and click through all 5 pages plus the login flow, confirming no
  horizontal scrolling anywhere, the bottom tab bar is reachable and
  correctly highlights the active page, and it doesn't visually collide
  with the browser's own bottom chrome (a reasonable proxy for the real
  iPhone home indicator, though the actual `env(safe-area-inset-bottom)`
  behavior can only be fully confirmed on a real device or Safari's
  device simulator).
