# Mobile Tab Bar Icons — Design

## Purpose

The mobile bottom tab bar (`portal/src/app/(dashboard)/MobileTabBar.tsx`) currently
shows text-only labels for its 5 links. Add an icon above each label, matching
the Sefed brand's existing color tokens and each link's meaning.

## Approach

Add `lucide-react` as a new dependency — small, tree-shakeable (only the 5
icons actually imported get bundled), MIT licensed. Lucide icons render with
`stroke="currentColor"` by default, so wrapping each icon in the tab link's
existing `text-accent` (active) / `text-ink-soft` (inactive) className
automatically tints it — no separate active/inactive icon logic needed.

`portal/src/app/(dashboard)/navLinksData.ts`'s `NavLink` type gains one new
required field: `icon: LucideIcon`. This is the shared data file both
`NavLinks.tsx` (desktop) and `MobileTabBar.tsx` (mobile) import — desktop
stays text-only and simply doesn't reference the new field, so adding it is
non-breaking for that component.

Icon-per-link mapping:

| Link | Icon | Rationale |
|---|---|---|
| Dashboard | `LayoutDashboard` | Grid/overview, standard dashboard icon |
| Attendance | `Clock` | The page is about punch times (in/out) |
| Staff (Employees) | `Users` | People |
| Shifts | `Calendar` | Scheduling |
| Reports | `FileBarChart` | A report document with data in it |

`MobileTabBar.tsx`'s per-link markup changes from a single text node to a
small vertical stack: the icon (rendered at `20`px, via Lucide's `size` prop)
above the existing label text, with a small gap between them
(`flex flex-col items-center gap-0.5`). The link's existing padding,
min-height, and active/inactive color classes are unchanged — the icon
inherits the color automatically.

## Testing

`MobileTabBar.test.tsx`'s existing two tests (mobile-label fallback,
active-tab highlighting) continue to pass unmodified — they assert on the
link's accessible name and className, neither of which changes. One new
test confirms each nav link actually renders an `<svg>` icon (catching a
typo'd icon reference or a link accidentally left without one).

## Scope

Touch exactly: `portal/package.json` (new dependency), `navLinksData.ts`
(add `icon` field to all 5 entries), `MobileTabBar.tsx` (render the icon),
`MobileTabBar.test.tsx` (one new test). `NavLinks.tsx` (desktop) is
unaffected and not modified.
