using System.Windows;
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using AttendanceAgent.ViewModels;
using Xunit;

namespace AttendanceAgent.Tests;

public class MainViewModelTests
{
    [Fact]
    public async Task Punch_OnSuccess_ClearsEmployeeCodeAndSetsStatus()
    {
        var capture = new FakeCaptureService { Result = new PunchResult(true, "Punch recorded for Jane Doe.") };
        var vm = new MainViewModel(capture, new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("", vm.EmployeeCode);
        Assert.Equal("Punch recorded for Jane Doe.", vm.StatusMessage);
    }

    [Fact]
    public async Task Punch_OnFailure_KeepsEmployeeCodeAndSetsStatus()
    {
        var capture = new FakeCaptureService { Result = new PunchResult(false, "Fingerprint did not match.") };
        var vm = new MainViewModel(capture, new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("E001", vm.EmployeeCode);
        Assert.Equal("Fingerprint did not match.", vm.StatusMessage);
    }

    [Fact]
    public async Task AdminLogin_CorrectTenantAdminCredentials_SwitchesToEnrollPanel()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
    }

    [Fact]
    public async Task AdminLogin_MatchingTenant_SwitchesToEnrollPanel()
    {
        var tenantId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
    }

    [Fact]
    public async Task AdminLogin_DifferentTenantThanStation_StaysOnPunchPanelWithError()
    {
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", Guid.NewGuid()), StationTenantId = Guid.NewGuid() };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@othertenant.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
        Assert.Equal("Admin login failed.", vm.StatusMessage);
    }

    [Fact]
    public async Task AdminLogin_StationTenantUnknown_StaysOnPunchPanelWithError()
    {
        // GetStationTenantIdAsync returning null (network failure, station-key rejected, etc.) must
        // fail closed — an admin login with no determinable station tenant must NOT be treated as a
        // match just because there's nothing to contradict it.
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", Guid.NewGuid()), StationTenantId = null };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal("Admin login failed.", vm.StatusMessage);
    }

    [Fact]
    public async Task AdminLogin_WrongRole_StaysOnPunchPanelWithError()
    {
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("Operator") };
        var prompt = new FakeAdminCredentialPrompt { Result = ("operator@zak.local", "ChangeMe123!") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
        Assert.Equal("Admin login failed.", vm.StatusMessage);
    }

    [Fact]
    public async Task AdminLogin_InvalidCredentials_StaysOnPunchPanelWithError()
    {
        var api = new FakeBackendApiClient { LoginResult = null };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "wrong-password") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal("Admin login failed.", vm.StatusMessage);
    }

    [Fact]
    public async Task AdminLogin_PromptCancelled_DoesNothing()
    {
        var api = new FakeBackendApiClient();
        var prompt = new FakeAdminCredentialPrompt { Result = null };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal("", vm.StatusMessage);
    }

    [Fact]
    public async Task StartEnrollment_OnSuccess_ReturnsToPunchPanelWithMessage()
    {
        var enrollment = new FakeEnrollmentService { Result = new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate.") };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";

        await vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.Equal("Fingerprint enrolled for Yoseph Addisu Abate.", vm.StatusMessage);
        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    }

    [Fact]
    public async Task PunchAndEnrollment_CannotRunConcurrently_OnTheSharedDevice()
    {
        var pending = new TaskCompletionSource<EnrollmentResult>();
        var enrollment = new FakeEnrollmentService { PendingCompletion = pending };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";

        var enrollTask = vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.False(vm.PunchCommand.CanExecute("In"));
        Assert.False(vm.StartEnrollmentCommand.CanExecute(null));

        pending.SetResult(new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate."));
        await enrollTask;

        Assert.True(vm.PunchCommand.CanExecute("In"));
        Assert.True(vm.StartEnrollmentCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelEnrollment_ActuallyCancelsTheRunningEnrollment_AndReturnsToPunchPanel()
    {
        var pending = new TaskCompletionSource<EnrollmentResult>();
        var enrollment = new FakeEnrollmentService { PendingCompletion = pending };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";

        var enrollTask = vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.True(vm.StartEnrollmentCancelCommand.CanExecute(null));
        vm.StartEnrollmentCancelCommand.Execute(null);
        await enrollTask;

        Assert.True(enrollment.LastCancellationToken!.Value.IsCancellationRequested);
        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    }

    [Fact]
    public async Task PunchAndEnrollment_CannotRunConcurrently_TheOtherDirection()
    {
        // Task 8 proved enrollment blocks a punch; this proves the reverse — a punch in progress
        // must also block starting an enrollment, since both share the same device instance.
        var pending = new TaskCompletionSource<PunchResult>();
        var capture = new FakeCaptureService { PendingCompletion = pending };
        var vm = new MainViewModel(capture, new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EmployeeCode = "E001";

        var punchTask = vm.PunchCommand.ExecuteAsync("In");

        Assert.False(vm.StartEnrollmentCommand.CanExecute(null));

        pending.SetResult(new PunchResult(true, "Punch recorded for Jane Doe."));
        await punchTask;

        Assert.True(vm.StartEnrollmentCommand.CanExecute(null));
    }

    [Fact]
    public async Task Punch_CaptureServiceThrows_StillClearsIsDeviceBusy()
    {
        // The "stuck busy forever" failure mode: if PunchAsync's finally didn't run, IsDeviceBusy
        // would stay true and both PunchCommand and StartEnrollmentCommand would be permanently
        // disabled after a single unexpected exception.
        var capture = new FakeCaptureService { ThrowOnCapture = true };
        var vm = new MainViewModel(capture, new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EmployeeCode = "E001";

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PunchCommand.ExecuteAsync("In"));

        Assert.True(vm.PunchCommand.CanExecute("In"));
        Assert.True(vm.StartEnrollmentCommand.CanExecute(null));
    }

    [Fact]
    public void ExitAdminMode_WhileIdleOnEnrollPanel_ReturnsToPunchPanel()
    {
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";
        vm.PunchPanelVisibility = Visibility.Collapsed;
        vm.EnrollPanelVisibility = Visibility.Visible;

        Assert.True(vm.ExitAdminModeCommand.CanExecute(null));
        vm.ExitAdminModeCommand.Execute(null);

        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    }

    [Fact]
    public async Task ExitAdminMode_WhileEnrollmentRunning_IsDisabled()
    {
        var pending = new TaskCompletionSource<EnrollmentResult>();
        var enrollment = new FakeEnrollmentService { PendingCompletion = pending };
        var vm = new MainViewModel(new FakeCaptureService(), enrollment, new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";

        var enrollTask = vm.StartEnrollmentCommand.ExecuteAsync(null);

        Assert.False(vm.ExitAdminModeCommand.CanExecute(null));

        pending.SetResult(new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate."));
        await enrollTask;

        Assert.True(vm.ExitAdminModeCommand.CanExecute(null));
    }
}
