# Desktop Agent: Admin/Punch Tabs — Design

## Purpose

Today, clicking "Admin" prompts for credentials and swaps the whole window
to the Enroll panel; when `StartEnrollmentAsync` finishes — success,
failure, *or cancellation* — its `finally` block unconditionally flips back
to the Punch panel. A failed enrollment (e.g. a bad capture) therefore
forces a full re-authentication just to retry. This reworks the two panels
into two persistent tabs — Punch and Admin — so enrollment attempts don't
lose the admin session, while still re-locking Admin when you deliberately
step away from it.

## Approach

Collapse the existing `PunchPanelVisibility`/`EnrollPanelVisibility` pair
(today two independent `[ObservableProperty] Visibility` fields) into one
source of truth, `IsAdminTabActive` (bool), with `PunchPanelVisibility` and
`EnrollPanelVisibility` becoming computed properties derived from it via
`[NotifyPropertyChangedFor]`. Existing XAML `Visibility="{Binding
PunchPanelVisibility}"` bindings are unchanged — only what drives them
changes. Being on the Admin tab and being "unlocked" are the same state
under this design (switching to Punch always re-locks; there's no separate
locked-but-visible Admin tab state), so one bool is sufficient — no second
`IsAdminUnlocked` flag needed.

**`AdminLoginAsync`** (bound to a new "Admin" tab-strip button, replacing
today's inline Admin button) keeps its existing credential-prompt and
tenant-matching logic verbatim. Its only change: on success it sets
`IsAdminTabActive = true` instead of flipping the two old Visibility
fields directly. Its `CanExecute` gains `&& !IsAdminTabActive` alongside
the existing `CanUseDevice()` — so the button auto-disables (visually
muted, via the same `IsEnabled=false` → `Opacity=0.5` trigger already used
elsewhere) while you're already on the Admin tab, and clicking it while
already there can't trigger a redundant re-prompt.

**`StartEnrollmentAsync`**'s `finally` block no longer touches tab state at
all — success, failure, and cancellation all leave you on the Admin tab.
It still clears `EnrollProgressMessage` always, but now only clears
`EnrollEmployeeCode` **on success** (`result?.Success == true`) — a failed
attempt keeps the code populated so retrying doesn't require retyping it.
(`result` is declared nullable before the `try` so the `finally` block can
safely check it without an unreachable non-null path — `EnrollAsync` is
documented to always return a result rather than throw, but this keeps the
method's own contract self-contained rather than relying on that.)

**`ExitAdminModeCommand`/the "Back" button are removed entirely**, superseded
by a new **`SelectPunchTabCommand`** (bound to a new "Punch" tab-strip
button): clears `EnrollEmployeeCode`/`EnrollProgressMessage` and sets
`IsAdminTabActive = false` — this is the re-lock point. Gated on
`CanUseDevice() && IsAdminTabActive` (can't switch away mid-enrollment,
same protection `ExitAdminModeCommand` had; disabled while already on
Punch, mirroring the Admin button's self-disable). The `isDeviceBusy`
field's `[NotifyCanExecuteChangedFor]` list swaps
`ExitAdminModeCommand` for `SelectPunchTabCommand`.

## UI

`MainWindow.xaml` gains a new tab-strip row between the header and the
content area (`Grid.RowDefinitions`: header `64`, tab strip `Auto`,
content `*`) — two plain buttons, "Punch" (→ `SelectPunchTabCommand`) and
"Admin" (→ `AdminLoginCommand`), each auto-disabling via its own
`CanExecute` when it's the currently-active tab. The old inline "Admin"
button (inside the Punch panel) and "Back" button (inside the Enroll
panel) are removed — the tab strip replaces both. Window height increases
from `480` to `520` to keep the same content breathing room with the new
row added; width unchanged.

## Testing

Existing `MainViewModelTests.cs` needs updates matching the above (not a
rewrite — most of the 18 existing tests are unaffected):
- Tests that directly assign `vm.PunchPanelVisibility = ...` (no longer
  possible — the property becomes computed/read-only) get rewritten to
  drive state through the real commands instead.
- `StartEnrollment_OnSuccess_ReturnsToPunchPanelWithMessage` is renamed and
  rewritten to assert the new behavior: stays on the Admin tab, message
  set, `EnrollEmployeeCode` cleared.
- `CancelEnrollment_ActuallyCancelsTheRunningEnrollment_AndReturnsToPunchPanel`
  similarly renamed/rewritten: cancellation also stays on the Admin tab now
  (one uniform rule — enrollment completion of any kind never itself
  changes tabs).
- `ExitAdminMode_WhileIdleOnEnrollPanel_ReturnsToPunchPanel` and
  `ExitAdminMode_WhileEnrollmentRunning_IsDisabled` are renamed/rewritten
  against `SelectPunchTabCommand`, preserving the same two behaviors (works
  while idle on the Admin tab; disabled while an enrollment is running).
- The remaining `AdminLogin_*` tests (wrong tenant, wrong role, invalid
  credentials, prompt cancelled/thrown, gated by `CanUseDevice`) are
  unaffected — their assertions already check the *values* `Visibility.
  Visible`/`Collapsed` produce for the not-yet-unlocked state, which the
  computed properties still return identically for the same underlying
  `IsAdminTabActive == false`.
- New test: a failed enrollment keeps `EnrollEmployeeCode` populated (the
  one behavior with no prior equivalent test).

## Scope

Touch: `MainViewModel.cs`, `MainWindow.xaml`, `MainViewModelTests.cs`. No
change to `EnrollmentService.cs`, `IAdminCredentialPrompt`/
`InputBoxAdminCredentialPrompt.cs`, or any vendor device code — this is
purely a panel-state/UI rework, not a change to what enrollment does or how
credentials are verified.
