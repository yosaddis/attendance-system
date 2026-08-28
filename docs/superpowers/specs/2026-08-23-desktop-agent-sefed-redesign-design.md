# Desktop Agent Visual Redesign (Sefed Brand) — Design Spec

## Context

The desktop agent (`agent/src/AttendanceAgent`) is a WPF kiosk app currently using default system styling — no colors, no branding, a small (420×320) fixed window. This spec applies the same Sefed Systems brand already used for the portal redesign (`docs/superpowers/specs/2026-08-23-portal-sefed-redesign-design.md`), adapted to WPF/XAML, and resizes the window for touch-friendly kiosk use.

## Goals

- Apply the same brand tokens (colors, Figtree/Chakra Petch typography, logo) used in the portal, via a WPF `ResourceDictionary` — the closest WPF equivalent to the portal's Tailwind `@theme` tokens.
- Resize the window to 560×480 (fixed, non-resizable — unchanged from today's `ResizeMode` behavior, just larger) with touch-friendly button sizing (~48px tall, generous padding).
- No behavior change — every `Command`/`Binding` in `MainWindow.xaml` stays wired to the exact same `MainViewModel` members; this is visual-only, same constraint as the portal spec.
- "Sefed Attendance" replaces "ZAK Attendance" in the window's `Title` (the only remaining occurrence after the portal work — the portal and agent were the only two surfaces with this string).

## Design Tokens (WPF `ResourceDictionary`)

Same hex values as the portal, expressed as WPF `SolidColorBrush` resources in a dictionary merged into `App.xaml`:

| Resource key | Value | Use |
|---|---|---|
| `SurfaceBrush` | `#FFFFFF` | Card/input backgrounds |
| `SurfaceMutedBrush` | `#F4F6F6` | Window background |
| `InkBrush` | `#174249` | Primary text, header band background |
| `InkSoftBrush` | `#3C6066` | Secondary text/labels |
| `BorderBrush` (careful: WPF's `Border` element is unrelated to a brush named `BorderBrush` — see Task 1's note on avoiding a name collision with `Control.BorderBrush`, the built-in dependency property) | `#DADADA` | Input/button borders |
| `AccentBrush` | `#E2B260` | Primary button fill, focus highlight, active states |
| `AccentHoverBrush` | `#D0822F` | Hover state on accent-filled elements |
| `AccentInkBrush` | `#174249` | Text on top of accent-filled buttons |
| `DangerBrush` | `#DC2626` | Error/failure status text |
| `DangerBackgroundBrush` | `#FEF2F2` | (not used in this small a UI today — kept for parity with the portal token list in case a future status callout needs it) |
| `FigtreeFont` (FontFamily resource) | embedded `Figtree` (reuse the same `.ttf` files already copied into the portal's `public/fonts/`, copied again into this project) | All body text |
| `ChakraPetchFont` (FontFamily resource) | embedded `Chakra Petch` | The header's "ATTENDANCE" sub-label only, matching the portal's usage |

## Layout

- `MainWindow`: `Height="480" Width="560"`, `ResizeMode` unchanged from today (not specified today, meaning WPF's default `CanResize` — out of scope to change; only the fixed starting size grows).
- A new header `Border` (height ~64px) docked to the top of the window, `Background="{StaticResource InkBrush}"`, containing: the Sefed icon (light-on-dark variant, same PNG already used in the portal's nav) + "Sefed" text (white, Figtree) + "ATTENDANCE" sub-label (gold, Chakra Petch, letter-spaced) — visually the same lockup as the portal nav, just in XAML instead of JSX.
- Below the header, the existing two `StackPanel`s (Punch/Enroll, toggled by the same `Visibility` bindings) get restyled in place — no structural/binding changes, only `Style`/margin/font updates.

## Styles

Defined as implicit WPF `Style`s (`TargetType="Button"` / `TargetType="TextBox"` with no `x:Key`, so they apply automatically to every button/textbox in the window without per-element `Style="{StaticResource ...}"` references) in the same merged `ResourceDictionary`:

- `TextBox`: `BorderBrush="{StaticResource BorderBrush}"`, `BorderThickness="1"`, rounded via a `ControlTemplate` (`Border` with `CornerRadius="6"` wrapping a `ScrollViewer` — WPF `TextBox` has no native corner-radius property, so the default template must be replaced), padding `8,6`, font size `16`. A `Trigger` on `IsFocused="True"` swaps the border to `AccentBrush` with `BorderThickness="2"`.
- `Button`: default style is a bordered, `SurfaceBrush`-filled rounded rectangle (`CornerRadius="8"`) with `InkBrush` text, font size `15`, min height `48`, padding `16,10`. A `Trigger` on `IsMouseOver="True"` swaps the border to `AccentBrush`. This is the DEFAULT for the 4 punch buttons + Cancel/Back (no single one is more important than the others).
- A named style `PrimaryButtonStyle` (`x:Key="PrimaryButtonStyle"`, explicitly applied only to "Start Enrollment") uses `AccentBrush` fill, `AccentInkBrush` text, and `AccentHoverBrush` on `IsMouseOver`.
- The small "Admin" button keeps the default `Button` style but at a smaller `FontSize="12"` and reduced padding, applied via a local style override rather than a second named style (it's still fundamentally "a button," not a visually distinct category).

## Explicitly Out of Scope

- Any change to `MainViewModel.cs`, commands, or bindings — this spec touches only `.xaml` files (`MainWindow.xaml`, `App.xaml`) plus a new resource dictionary file and font/icon asset files.
- A true `.ico` file for the compiled `.exe`'s Explorer/file-icon (would need external icon conversion tooling not available in this environment) — the window's title-bar/taskbar icon is set directly from the existing PNG instead (`Window.Icon` accepts any `ImageSource`-compatible format, not just `.ico`), which covers everything visible while the app is actually running.
- Any change to the first-run `InputBox` prompts or the admin-credential `InputBox` dialogs (`Microsoft.VisualBasic.Interaction.InputBox`) — these are OS-native dialogs outside the app's own XAML and can't be restyled without replacing them with custom WPF dialogs, which is a functional change, not a visual one, and was already flagged as a separate, deliberately-deferred item in the fingerprint-enrollment branch's own README notes.

## Self-Review Notes

- Placeholder scan: none — every token has a concrete hex/resource value, every style has concrete property values.
- Internal consistency: uses the exact same 10 color values as the portal spec, single source of truth is "the brand guideline," not re-derived.
- Scope check: single window, single resource dictionary, appropriately scoped for one plan.
- Ambiguity check: flagged the `BorderBrush` naming collision risk explicitly (WPF's `Control.BorderBrush` dependency property vs. a custom resource of the same name) so the plan defines the custom resource under a different key (`BorderColorBrush`) to avoid confusing XAML resource lookups with the built-in property — resolved here rather than left for the implementer to discover.
