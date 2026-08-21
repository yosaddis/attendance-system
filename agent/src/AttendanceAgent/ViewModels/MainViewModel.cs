using System.Windows;
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AttendanceAgent.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPunchCaptureService _captureService;
    private readonly IEnrollmentService _enrollmentService;
    private readonly IBackendApiClient _api;
    private readonly IFingerprintDevice _device;
    private readonly IFingerprintEnroller _enroller;
    private readonly IAdminCredentialPrompt _prompt;

    [ObservableProperty]
    private string employeeCode = "";

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private string enrollEmployeeCode = "";

    [ObservableProperty]
    private string enrollProgressMessage = "";

    [ObservableProperty]
    private Visibility punchPanelVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility enrollPanelVisibility = Visibility.Collapsed;

    // A punch and an enrollment must never run concurrently — both drive the same Singleton
    // IFingerprintDevice instance (see HostComposition.ConfigureServices), which holds mutable
    // state (e.g. ZkFingerprintDevice._devHandle, or SecuGen's single shared FIRTextData
    // property). Proven live during the whole-branch review: a punch during an in-flight
    // enrollment caused concurrent Acquire/Release calls on the same device instance. Both
    // PunchCommand and StartEnrollmentCommand gate on CanUseDevice, and
    // NotifyCanExecuteChangedFor re-evaluates both commands' CanExecute whenever this flips.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartEnrollmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExitAdminModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdminLoginCommand))]
    private bool isDeviceBusy;

    public MainViewModel(
        IPunchCaptureService captureService,
        IEnrollmentService enrollmentService,
        IBackendApiClient api,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        IAdminCredentialPrompt prompt)
    {
        _captureService = captureService;
        _enrollmentService = enrollmentService;
        _api = api;
        _device = device;
        _enroller = enroller;
        _prompt = prompt;
    }

    private bool CanUseDevice() => !IsDeviceBusy;

    // Without this, an admin who unlocks the Enroll panel and is called away (or simply changes
    // their mind before clicking "Start Enrollment") leaves the station with NO reachable punch
    // UI — the punch panel is Collapsed (WPF does not hit-test collapsed elements), and
    // StartEnrollmentCancelCommand is only enabled while an enrollment is actually running.
    // Reproduced live during Task 8's review: idle on the Enroll panel,
    // StartEnrollmentCancelCommand.CanExecute(null) is false. Gated on CanUseDevice (not
    // unconditionally enabled) so it can't be used to bypass a running enrollment — exiting
    // while one is genuinely in flight must still go through Cancel.
    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    private void ExitAdminMode()
    {
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        PunchPanelVisibility = Visibility.Visible;
        EnrollPanelVisibility = Visibility.Collapsed;
    }

    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    private async Task PunchAsync(string punchType)
    {
        IsDeviceBusy = true;
        try
        {
            var result = await _captureService.CapturePunchAsync(EmployeeCode, punchType, _device);
            StatusMessage = result.Message;
            if (result.Success) EmployeeCode = "";
        }
        finally
        {
            IsDeviceBusy = false;
        }
    }

    // Gated on CanUseDevice for the same reason PunchCommand/StartEnrollmentCommand are: without
    // this, an admin login completing while a punch is genuinely in-flight unconditionally flips
    // the panels, momentarily unlocking the Enroll panel with every one of its buttons disabled
    // (StartEnrollmentCommand/ExitAdminModeCommand both also gate on CanUseDevice) until the
    // punch finishes — the operator sees a station that looks unlocked but does nothing.
    [RelayCommand(CanExecute = nameof(CanUseDevice))]
    private async Task AdminLoginAsync()
    {
        (string Email, string Password)? credentials;
        try
        {
            credentials = _prompt.PromptForCredentials();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not show the admin login prompt: {ex.Message}";
            return;
        }

        if (credentials is null) return;

        var login = await _api.LoginAsync(credentials.Value.Email, credentials.Value.Password);
        if (login is null || login.Role != "TenantAdmin")
        {
            StatusMessage = "Admin login failed.";
            return;
        }

        // Role alone isn't enough: a TenantAdmin for ANY tenant would otherwise unlock enrollment
        // on THIS station regardless of which company owns it. GetStationTenantIdAsync returning
        // null (network failure, etc.) fails closed — treated the same as a mismatch, not as "no
        // reason to reject."
        var stationTenantId = await _api.GetStationTenantIdAsync();
        if (stationTenantId is null || login.TenantId != stationTenantId)
        {
            StatusMessage = "Admin login failed.";
            return;
        }

        StatusMessage = "";
        PunchPanelVisibility = Visibility.Collapsed;
        EnrollPanelVisibility = Visibility.Visible;
    }

    // IncludeCancelCommand generates a companion StartEnrollmentCancelCommand, automatically
    // enabled only while this command is actually running (CommunityToolkit.Mvvm tracks this via
    // the command's IsRunning state) — exactly the semantics a Cancel button needs, and strictly
    // better than the old hand-written CancelEnrollmentCommand, which could be invoked at any
    // time and never actually stopped the running enrollment (no CancellationToken was wired to
    // it at all). The panel reset below runs once EnrollAsync itself returns — whether it
    // completed, failed, or was cancelled — never eagerly on the button click, so the UI can't
    // flip back to the punch panel while the enrollment still owns the device.
    [RelayCommand(CanExecute = nameof(CanUseDevice), IncludeCancelCommand = true)]
    private async Task StartEnrollmentAsync(CancellationToken ct)
    {
        IsDeviceBusy = true;
        try
        {
            var result = await _enrollmentService.EnrollAsync(
                EnrollEmployeeCode,
                _device,
                _enroller,
                (i, total) => EnrollProgressMessage = $"Place your finger ({i} of {total})",
                ct);

            StatusMessage = result.Message;
        }
        finally
        {
            EnrollEmployeeCode = "";
            EnrollProgressMessage = "";
            PunchPanelVisibility = Visibility.Visible;
            EnrollPanelVisibility = Visibility.Collapsed;
            IsDeviceBusy = false;
        }
    }
}
