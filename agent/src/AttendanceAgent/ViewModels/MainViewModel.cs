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
    [NotifyPropertyChangedFor(nameof(PunchPanelVisibility))]
    [NotifyPropertyChangedFor(nameof(EnrollPanelVisibility))]
    [NotifyCanExecuteChangedFor(nameof(AdminLoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectPunchTabCommand))]
    private bool isAdminTabActive;

    public Visibility PunchPanelVisibility => IsAdminTabActive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EnrollPanelVisibility => IsAdminTabActive ? Visibility.Visible : Visibility.Collapsed;

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
    [NotifyCanExecuteChangedFor(nameof(SelectPunchTabCommand))]
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

    private bool CanLoginAsAdmin() => CanUseDevice() && !IsAdminTabActive;

    private bool CanSelectPunchTab() => CanUseDevice() && IsAdminTabActive;

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
        StatusMessage = "";
        IsAdminTabActive = false;
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

    // Gated on CanUseDevice so a login can't be STARTED while a punch or enrollment already owns
    // the device, or re-triggered while already on the Admin tab. This does not fully close the
    // gap: AdminLoginAsync never sets IsDeviceBusy
    // itself, so a punch that starts DURING an already-in-progress login's two awaits (LoginAsync,
    // GetStationTenantIdAsync) can still land its own IsDeviceBusy=true/false around the login's
    // unconditional panel flip below — momentarily unlocking the Enroll panel with every one of
    // its buttons disabled (StartEnrollmentCommand/SelectPunchTabCommand both gate on CanUseDevice
    // too) until the punch finishes. That residual ordering is benign and self-healing (the
    // operator sees a station that looks unlocked but does nothing, for the punch's duration only)
    // — this gate exists to prevent the more common case, not to guarantee every ordering.
    [RelayCommand(CanExecute = nameof(CanLoginAsAdmin))]
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
        IsAdminTabActive = true;
    }

    // IncludeCancelCommand generates a companion StartEnrollmentCancelCommand, automatically
    // enabled only while this command is actually running (CommunityToolkit.Mvvm tracks this via
    // the command's IsRunning state) — exactly the semantics a Cancel button needs, and strictly
    // better than the old hand-written CancelEnrollmentCommand, which could be invoked at any
    // time and never actually stopped the running enrollment (no CancellationToken was wired to
    // it at all). Enrollment completion of any kind — success, failure, or cancellation — never
    // itself changes which tab is active; only SelectPunchTab re-locks the Admin tab. This
    // method's finally block only clears transient enrollment state (EnrollProgressMessage
    // always, EnrollEmployeeCode only on success) and releases the device lock.
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
}
