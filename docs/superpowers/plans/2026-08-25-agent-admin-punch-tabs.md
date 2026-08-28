# Desktop Agent: Admin/Punch Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Punch/Enroll panel-swap driven by an admin credential prompt with two persistent tabs (Punch, Admin) so a failed or cancelled enrollment doesn't force re-authentication.

**Architecture:** Collapse `PunchPanelVisibility`/`EnrollPanelVisibility` (today two independently-settable `[ObservableProperty]` fields) into one source of truth, `IsAdminTabActive` (bool); the two Visibility properties become computed, read-only, derived via `[NotifyPropertyChangedFor]`. `ExitAdminModeCommand`/the "Back" button are replaced by `SelectPunchTabCommand`, the deliberate re-lock point. `StartEnrollmentAsync`'s `finally` no longer touches tab state at all.

**Tech Stack:** WPF / .NET 8, CommunityToolkit.Mvvm source generators, xUnit.

## Global Constraints

- Being on the Admin tab and being "unlocked" are the same state — no separate locked-but-visible Admin tab state, so one bool (`IsAdminTabActive`) is sufficient.
- `EnrollEmployeeCode` is cleared only when an enrollment attempt succeeds (`result?.Success == true`) — a failed *or cancelled* attempt keeps it populated so retrying doesn't require retyping it. This is a deliberate behavior change from today (today's `finally` clears it unconditionally).
- No change to `EnrollmentService.cs`, `IAdminCredentialPrompt`/`InputBoxAdminCredentialPrompt.cs`, or any vendor device code — this is purely a panel-state/UI rework.

---

### Task 1: MainViewModel rework — Admin/Punch tab state

**Files:**
- Modify: `agent/src/AttendanceAgent/ViewModels/MainViewModel.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs`

**Interfaces:**
- Produces: `bool IsAdminTabActive` (settable, generated), `Visibility PunchPanelVisibility` (computed, read-only), `Visibility EnrollPanelVisibility` (computed, read-only), `IRelayCommand SelectPunchTabCommand` (replaces `ExitAdminModeCommand`, which is removed). `AdminLoginCommand`, `StartEnrollmentCommand`, `StartEnrollmentCancelCommand`, `PunchCommand` keep their existing names/signatures.

- [ ] **Step 1: Rewrite the affected tests to the new expected behavior**

In `agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs`, replace the four tests below (by name) with their rewritten versions, and add the one new test. Every other test in the file (Punch_*, the four AdminLogin_* wrong-tenant/role/credentials/prompt-cancelled tests, AdminLogin_CorrectTenantAdminCredentials_SwitchesToEnrollPanel, AdminLogin_MatchingTenant_SwitchesToEnrollPanel, AdminLogin_IsGatedByCanUseDevice_DisabledWhileAPunchIsInFlight, AdminLogin_PromptThrows_ReturnsFailureMessage_DoesNotEscape, PunchAndEnrollment_CannotRunConcurrently_*, Punch_CaptureServiceThrows_StillClearsIsDeviceBusy) stays exactly as-is — their assertions already check the *values* the computed properties still produce identically.

Replace `StartEnrollment_OnSuccess_ReturnsToPunchPanelWithMessage` (currently lines 134-147) with:

```csharp
    [Fact]
    public async Task StartEnrollment_OnSuccess_StaysOnAdminTabWithMessage()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var enrollment = new FakeEnrollmentService { Result = new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate.") };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);
        await vm.AdminLoginCommand.ExecuteAsync(null);
        vm.EnrollEmployeeCode = "E002";

        await vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.Equal("Fingerprint enrolled for Yoseph Addisu Abate.", vm.StatusMessage);
        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
    }
```

Replace `CancelEnrollment_ActuallyCancelsTheRunningEnrollment_AndReturnsToPunchPanel` (currently lines 169-187) with:

```csharp
    [Fact]
    public async Task CancelEnrollment_ActuallyCancelsTheRunningEnrollment_AndStaysOnAdminTab()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var pending = new TaskCompletionSource<EnrollmentResult>();
        var enrollment = new FakeEnrollmentService { PendingCompletion = pending };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);
        await vm.AdminLoginCommand.ExecuteAsync(null);
        vm.EnrollEmployeeCode = "E002";

        var enrollTask = vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.True(vm.StartEnrollmentCancelCommand.CanExecute(null));
        vm.StartEnrollmentCancelCommand.Execute(null);
        await enrollTask;

        // FakeEnrollmentService resolves a cancelled attempt with EnrollmentResult(false, "Enrollment
        // cancelled."), not an exception — so under the new "only clear on success" rule,
        // EnrollEmployeeCode is deliberately RETAINED here (unlike the old unconditional clear).
        Assert.True(enrollment.LastCancellationToken!.Value.IsCancellationRequested);
        Assert.Equal("Enrollment cancelled.", vm.StatusMessage);
        Assert.Equal("E002", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
    }
```

Replace `ExitAdminMode_WhileIdleOnEnrollPanel_ReturnsToPunchPanel` (currently lines 225-239) with:

```csharp
    [Fact]
    public async Task SelectPunchTab_WhileIdleOnAdminTab_ReturnsToPunchTab()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);
        await vm.AdminLoginCommand.ExecuteAsync(null);
        vm.EnrollEmployeeCode = "E002";

        Assert.True(vm.SelectPunchTabCommand.CanExecute(null));
        vm.SelectPunchTabCommand.Execute(null);

        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    }
```

Replace `ExitAdminMode_WhileEnrollmentRunning_IsDisabled` (currently lines 241-257) with:

```csharp
    [Fact]
    public async Task SelectPunchTab_WhileEnrollmentRunning_IsDisabled()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var pending = new TaskCompletionSource<EnrollmentResult>();
        var enrollment = new FakeEnrollmentService { PendingCompletion = pending };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);
        await vm.AdminLoginCommand.ExecuteAsync(null);
        vm.EnrollEmployeeCode = "E002";

        var enrollTask = vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.False(vm.SelectPunchTabCommand.CanExecute(null));

        pending.SetResult(new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate."));
        await enrollTask;

        Assert.True(vm.SelectPunchTabCommand.CanExecute(null));
    }
```

Add this new test (place it directly after `StartEnrollment_OnSuccess_StaysOnAdminTabWithMessage`):

```csharp
    [Fact]
    public async Task StartEnrollment_OnFailure_KeepsEnrollEmployeeCodeAndStaysOnAdminTab()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var enrollment = new FakeEnrollmentService { Result = new EnrollmentResult(false, "Fingerprint capture failed — please try again.") };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);
        await vm.AdminLoginCommand.ExecuteAsync(null);
        vm.EnrollEmployeeCode = "E002";

        await vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.Equal("Fingerprint capture failed — please try again.", vm.StatusMessage);
        Assert.Equal("E002", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run (from `agent/`): `dotnet test`
Expected: FAIL to build — `vm.SelectPunchTabCommand` doesn't exist yet (still `ExitAdminModeCommand` in the ViewModel).

- [ ] **Step 3: Rewrite MainViewModel.cs**

Replace the `punchPanelVisibility`/`enrollPanelVisibility` fields (currently lines 31-35) with:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PunchPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(EnrollPanelVisibility))]
    [NotifyCanExecuteChangedFor(nameof(AdminLoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectPunchTabCommand))]
    private bool isAdminTabActive;

    public Visibility PunchPanelVisibility => IsAdminTabActive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EnrollPanelVisibility => IsAdminTabActive ? Visibility.Visible : Visibility.Collapsed;
```

In the `isDeviceBusy` field's attribute list (currently lines 44-49), replace `[NotifyCanExecuteChangedFor(nameof(ExitAdminModeCommand))]` with `[NotifyCanExecuteChangedFor(nameof(SelectPunchTabCommand))]`:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartEnrollmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectPunchTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdminLoginCommand))]
    private bool isDeviceBusy;
```

Replace `private bool CanUseDevice() => !IsDeviceBusy;` (currently line 67) with:

```csharp
    private bool CanUseDevice() => !IsDeviceBusy;

    private bool CanLoginAsAdmin() => CanUseDevice() && !IsAdminTabActive;

    private bool CanSelectPunchTab() => CanUseDevice() && IsAdminTabActive;
```

Replace the entire `ExitAdminMode` method (currently lines 69-84, including its doc comment) with:

```csharp
    // Without this, an admin who unlocks the Admin tab and is called away (or simply changes
    // their mind before clicking "Start Enrollment") leaves the station with NO reachable punch
    // UI — the punch panel is Collapsed (WPF does not hit-test collapsed elements), and
    // StartEnrollmentCancelCommand is only enabled while an enrollment is actually running. This
    // is the deliberate re-lock point: switching back to Punch always clears the Admin tab's
    // state. Gated on CanUseDevice (not unconditionally enabled) so it can't be used to bypass a
    // running enrollment — leaving while one is genuinely in flight must still go through Cancel.
    [RelayCommand(CanExecute = nameof(CanSelectPunchTab))]
    private void SelectPunchTab()
    {
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        IsAdminTabActive = false;
    }
```

Change the `AdminLoginAsync` command attribute (currently line 111) from `[RelayCommand(CanExecute = nameof(CanUseDevice))]` to `[RelayCommand(CanExecute = nameof(CanLoginAsAdmin))]`.

Replace the final three lines of `AdminLoginAsync` (currently lines 145-147):

```csharp
        StatusMessage = "";
        PunchPanelVisibility = Visibility.Collapsed;
        EnrollPanelVisibility = Visibility.Visible;
```

with:

```csharp
        StatusMessage = "";
        IsAdminTabActive = true;
```

Replace the body of `StartEnrollmentAsync` (currently lines 159-181) with:

```csharp
    [RelayCommand(CanExecute = nameof(CanUseDevice), IncludeCancelCommand = true)]
    private async Task StartEnrollmentAsync(CancellationToken ct)
    {
        IsDeviceBusy = true;
        EnrollmentResult? result = null;
        try
        {
            result = await _enrollmentService.EnrollAsync(
                EnrollEmployeeCode,
                _device,
                _enroller,
                (i, total) => EnrollProgressMessage = $"Place your finger ({i} of {total})",
                ct);

            StatusMessage = result.Message;
        }
        finally
        {
            EnrollProgressMessage = "";
            if (result?.Success == true) EnrollEmployeeCode = "";
            IsDeviceBusy = false;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS (all tests, including the 4 rewritten and 1 new)

- [ ] **Step 5: Commit**

```bash
git add agent/src/AttendanceAgent/ViewModels/MainViewModel.cs agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs
git commit -m "feat: rework agent Admin/Punch panels into persistent tabs"
```

---

### Task 2: MainWindow.xaml tab strip

**Files:**
- Modify: `agent/src/AttendanceAgent/MainWindow.xaml`

**Interfaces:**
- Consumes: `SelectPunchTabCommand`, `AdminLoginCommand`, `PunchPanelVisibility`, `EnrollPanelVisibility` from `MainViewModel` (Task 1).

- [ ] **Step 1: Replace MainWindow.xaml**

Replace the full contents of `agent/src/AttendanceAgent/MainWindow.xaml`:

```xml
<Window x:Class="AttendanceAgent.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Sefed Attendance" Height="520" Width="560"
        Icon="Assets/sefed-icon-dark.png"
        Background="{StaticResource SurfaceMutedBrush}"
        FontFamily="{StaticResource FigtreeFont}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="64" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{StaticResource InkBrush}">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="20,0">
                <Image Source="Assets/sefed-icon.png" Width="32" Height="32" />
                <StackPanel Margin="10,0,0,0" VerticalAlignment="Center">
                    <TextBlock Text="Sefed" Foreground="White" FontSize="16" FontWeight="Medium" />
                    <TextBlock Text="A T T E N D A N C E" Foreground="{StaticResource AccentBrush}"
                               FontFamily="{StaticResource ChakraPetchFont}" FontSize="10" />
                </StackPanel>
            </StackPanel>
        </Border>

        <Border Grid.Row="1" Background="{StaticResource SurfaceBrush}">
            <StackPanel Orientation="Horizontal" Margin="20,10">
                <Button Content="Punch" Command="{Binding SelectPunchTabCommand}"
                        Margin="0,0,10,0" MinWidth="90" Padding="10,6" MinHeight="0" FontSize="12" />
                <Button Content="Admin" Command="{Binding AdminLoginCommand}"
                        MinWidth="90" Padding="10,6" MinHeight="0" FontSize="12" />
            </StackPanel>
        </Border>

        <Grid Grid.Row="2">
            <StackPanel Margin="24" Visibility="{Binding PunchPanelVisibility}">
                <TextBlock Text="Employee ID / PIN" Margin="0,0,0,6" FontSize="14"
                           Foreground="{StaticResource InkSoftBrush}" />
                <TextBox Text="{Binding EmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,20" />
                <WrapPanel>
                    <Button Content="IN" Command="{Binding PunchCommand}" CommandParameter="In" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="BREAK OUT" Command="{Binding PunchCommand}" CommandParameter="BreakOut" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="BREAK IN" Command="{Binding PunchCommand}" CommandParameter="BreakIn" Margin="0,0,10,10" MinWidth="110" />
                    <Button Content="OUT" Command="{Binding PunchCommand}" CommandParameter="Out" Margin="0,0,10,10" MinWidth="110" />
                </WrapPanel>
                <TextBlock Text="{Binding StatusMessage}" Margin="0,20,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource InkBrush}" />
            </StackPanel>

            <StackPanel Margin="24" Visibility="{Binding EnrollPanelVisibility}">
                <TextBlock Text="Enroll fingerprint — Employee ID / PIN" Margin="0,0,0,6" FontSize="14"
                           Foreground="{StaticResource InkSoftBrush}" />
                <TextBox Text="{Binding EnrollEmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,20" />
                <WrapPanel>
                    <Button Content="Start Enrollment" Command="{Binding StartEnrollmentCommand}"
                            Style="{StaticResource PrimaryButtonStyle}" Margin="0,0,10,10" MinWidth="160" />
                    <Button Content="Cancel" Command="{Binding StartEnrollmentCancelCommand}" Margin="0,0,10,10" MinWidth="110" />
                </WrapPanel>
                <TextBlock Text="{Binding EnrollProgressMessage}" Margin="0,20,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource InkBrush}" />
                <TextBlock Text="{Binding StatusMessage}" Margin="0,8,0,0" TextWrapping="Wrap"
                           FontSize="14" Foreground="{StaticResource InkBrush}" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Build**

Run (from `agent/`): `dotnet build`
Expected: build succeeds (0 errors) — confirms the XAML's `{Binding SelectPunchTabCommand}` resolves against `MainViewModel` from Task 1.

- [ ] **Step 3: Manually verify in the running app**

Run: `dotnet run --project src/AttendanceAgent` (Debug/fake device is fine for this UI-only check — no real hardware needed).
- Confirm the window opens at the taller size with a visible tab strip between the header and the Punch panel.
- Click "Admin", enter any credentials in the prompt (the Debug fakes accept whatever `FakeAdminCredentialPrompt`/backend stub is wired for local testing) — confirm the Admin tab's Enroll panel appears and the "Admin" button is now visibly muted/disabled.
- On the Admin tab, click "Start Enrollment" and cancel it — confirm the app stays on the Admin tab (no forced return to Punch) and `EnrollEmployeeCode` is still populated for a retry.
- Click "Punch" — confirm it returns to the Punch panel and the "Punch" button is now visibly muted/disabled.

- [ ] **Step 4: Commit**

```bash
git add agent/src/AttendanceAgent/MainWindow.xaml
git commit -m "feat: add Punch/Admin tab strip to the agent window"
```
