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
        var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin") };
        var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

        await vm.AdminLoginCommand.ExecuteAsync(null);

        Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
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
    public void CancelEnrollment_ReturnsToPunchPanel()
    {
        var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
        vm.EnrollEmployeeCode = "E002";
        vm.EnrollPanelVisibility = Visibility.Visible;
        vm.PunchPanelVisibility = Visibility.Collapsed;

        vm.CancelEnrollmentCommand.Execute(null);

        Assert.Equal("", vm.EnrollEmployeeCode);
        Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
        Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    }
}
