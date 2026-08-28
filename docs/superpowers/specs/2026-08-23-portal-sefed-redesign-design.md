# Portal Visual Redesign (Sefed Brand) — Design Spec

## Context

The web portal (`portal/`) currently uses only default `create-next-app` Tailwind styling — plain borders, no color system, no typography treatment, no branding. This spec covers restyling it using Sefed Systems' actual brand guideline (`C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\BrandGuideline.pdf`, v01, Sept 2025).

As part of this work, the product's user-facing name changes from "ZAK Attendance" to **"Sefed Attendance"** in both the portal and the desktop agent (repo/folder names, code identifiers, and "ZAK" as an internal codename are unaffected — this is a display-string change only). The desktop agent's own visual redesign is a separate, later piece of work (out of scope here — portal only, per explicit sequencing decision).

## Goals

- Apply the real Sefed brand (colors, typography, logo, radial pattern motif) to the portal's login page and the three dashboard pages (Attendance, Employees, Shifts) plus the shared nav shell.
- Base theme is **light** with teal/gold as accents — not the all-dark surface shown in the brand deck's own marketing collateral. The dark teal is reserved for the nav bar and other deliberate accent surfaces, not page backgrounds, so data-dense tables and forms stay easy to read for all-day admin use.
- Rename the product to "Sefed Attendance" everywhere it's displayed to a user (page titles, headings, window titles) — not in code identifiers, file/folder names, or the repo name.

## Design Tokens

Defined once in `portal/src/app/globals.css` via Tailwind v4's `@theme` block, sourced from the brand guideline's actual primary/secondary palettes (page 16-17 of the PDF):

| Token | Value | Source | Use |
|---|---|---|---|
| `--color-surface` | `#FFFFFF` | — | Page/card backgrounds |
| `--color-surface-muted` | `#F4F6F6` | tinted off Bright Silver `#E8E8E9` | Page background behind cards, table header row fill |
| `--color-ink` | `#174249` | "Donaldson" (brand primary) | Primary text, nav bar background |
| `--color-ink-soft` | `#3C6066` | Cyan Shades ramp | Secondary/muted text, placeholder text |
| `--color-border` | `#DADADA` | Grey Tint ramp | Card/input/table borders and dividers |
| `--color-accent` | `#E2B260` | "Sandy Brown" (brand primary) | Primary buttons, active nav indicator, links, focus rings |
| `--color-accent-hover` | `#D0822F` | Sandy Brown ramp | Hover/active state on accent elements |
| `--color-accent-ink` | `#174249` | Donaldson | Text color ON TOP of accent-colored buttons (gold-on-white fails contrast; dark-teal-on-gold passes) |
| `--color-danger` | `#DC2626` (Tailwind red-600) | standard, not brand | Error text/callouts — deliberately NOT a brand color, so errors are visually distinct from brand chrome |
| `--color-danger-bg` | `#FEF2F2` (Tailwind red-50) | standard | Error callout background |
| `--font-sans` | Figtree (variable, self-hosted from the brand kit's own font files) | Brand Typography, p.19 | All UI text |
| `--font-display` | Chakra Petch (self-hosted) | Brand Typography, p.20 | Page-level `<h1>` headings and the nav's "ATTENDANCE" sub-label only — not body text or form labels |

Font files are copied from `C:\Users\Traveler\Documents\Work\Sefed systems\SefedSystemsBranding\ProjectFiles\Fonts\` into `portal/public/fonts/` (Figtree: Regular/Medium/SemiBold/Bold static weights; Chakra Petch: Regular/SemiBold) and loaded via `next/font/local`, not Google Fonts (avoids an external network dependency and matches the brand kit's own files exactly rather than a close public substitute).

The logo icon (`Logo/Icon/Icon-01.png` or the matching SVG if the icon-only SVG proves clean — verified against the actual brand pages during research, not the older/inconsistent SVG variants found alongside it in the same folder) is copied into `portal/public/sefed-icon.png`.

## Rename: "ZAK Attendance" → "Sefed Attendance"

Grep for the literal string `"ZAK Attendance"` across `portal/src` and update every user-visible occurrence (page `<title>`/metadata, login heading, any other display text). This is a display-string change only — no renaming of files, folders, npm package name, repo name, or the `ZAK`-branded desktop-agent identifiers (`AttendanceAgent` namespace, `AgentSettings`, etc.), all of which stay as-is. The desktop agent's own "ZAK Attendance" window title string gets the same display-string rename as a small follow-up (tracked, not part of this portal-only spec's file list, since the agent's fuller visual redesign is separate work) — call this out explicitly as a one-line follow-up rather than silently leaving it inconsistent.

## Nav Shell (`portal/src/app/(dashboard)/layout.tsx`)

- Full-width bar, `--color-ink` background.
- Left: `sefed-icon.png` (small, ~28px) + "Sefed" in `--font-sans` SemiBold, light grey/white, with "ATTENDANCE" beneath in `--font-display`, small-caps letter-spacing, `--color-accent` — mirroring the real logo's icon + "SYSTEMS" sub-label construction (brand guideline p.9/12), with the sub-label text swapped for the product name.
- Center-right: the three existing nav links (Attendance / Employees / Shifts), light grey by default; the active route gets a `--color-accent` underline/pill (determined via Next.js's current-path check, e.g. `usePathname()` in a small client component, or a server-side check against the route segment — implementation detail for the plan).
- Right: "Log out" as a ghost-style button (transparent, light grey text, `--color-accent` on hover), same `logout` server action as today — no behavior change.

## Login Page (`portal/src/app/login/page.tsx`)

- Full-height `--color-surface-muted` background, centered card.
- Card: `--color-surface` background, `--color-border` outline, soft shadow (`shadow-md` or similar Tailwind default — not a custom brand-specific shadow, brand guideline doesn't specify one), rounded corners (`rounded-lg`+).
- `sefed-icon.png` centered above the form.
- Heading: "Sign in to " in `--font-sans` regular + "Sefed Attendance" in `--font-display`, `--color-ink`.
- Inputs: `--color-surface` background, `--color-border` outline, `--color-accent` focus ring (replacing the default browser blue outline via Tailwind's `focus:ring`/`focus:border` utilities).
- Submit button: `--color-accent` background, `--color-accent-ink` text, `--color-accent-hover` on hover.
- Error message: existing 3-case logic (`unreachable`/`role`/generic) unchanged — only the presentation changes, from bare red text to a `--color-danger-bg` callout box with `--color-danger` text and a rounded border, so it reads as a deliberate alert rather than broken styling.

## Attendance / Employees / Shifts Pages

- Page heading: `--font-display`, `--color-ink`.
- Existing date-filter/create-form inputs get the same input treatment as the login page (consistency, not a new pattern).
- Table: wrapped in a `--color-surface` card (border + rounded corners, matching the login card language). Header row: `--color-surface-muted` background, `--color-ink` text. Body rows: `--color-border` bottom-divider between rows, subtle `--color-surface-muted` background on hover.
- Create forms (Employees, Shifts): same card/input/button treatment as login. Field-level validation errors (already wired server-side from earlier work) render directly under the relevant field in `--color-danger` text — no new validation LOGIC, this is presentation-only for errors that already exist.
- Buttons follow the same primary (`--color-accent` fill) treatment as the login submit button; no secondary/ghost button style is introduced unless a page already has more than one action needing visual hierarchy (a plan-time detail, not decided here since the current pages weren't inspected page-by-page for this).

## Explicitly Out of Scope

- The desktop WPF agent's visual redesign (separate spec, later — the user chose "portal first" when this branch of work was scoped).
- Dark-mode toggle (the user chose light-base-only, not the toggle option, during design discussion).
- Any new component library/dependency (Approach A — token-based Tailwind rebuild — was chosen over introducing shadcn/ui or similar).
- Any change to portal *behavior* (routing, data fetching, validation logic, auth) — this is a visual-only pass. The only functional-adjacent change is the literal product-name string rename described above.
- Redesigning the actual brand assets themselves (logo, patterns) — this spec only consumes the existing, approved brand kit as-is.

## Self-Review Notes

- Placeholder scan: none found — every section has concrete values (hex codes, font names, file paths), not "TBD".
- Internal consistency: the "light base, dark nav" split is stated once in Goals and applied consistently in every subsequent section (nav is the only `--color-ink`-background surface; every other section uses `--color-surface`/`--color-surface-muted`).
- Scope check: appropriately scoped for one implementation plan — 4 pages + 1 shared layout + a design-token setup step, all using the same token set, no sub-decomposition needed.
- Ambiguity check: the exact icon asset (PNG vs. SVG, and which of the several icon-folder variants) is flagged as needing verification at implementation time rather than guessed at now, since the branding folder contains multiple historical logo iterations alongside the final approved one — the plan should have the implementer confirm against the brand guideline PDF's own rendered pages before picking a file.
