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

    [RelayCommand]
    private async Task PunchAsync(string punchType)
    {
        var result = await _captureService.CapturePunchAsync(EmployeeCode, punchType, _device);
        StatusMessage = result.Message;
        if (result.Success) EmployeeCode = "";
    }

    [RelayCommand]
    private async Task AdminLoginAsync()
    {
        var credentials = _prompt.PromptForCredentials();
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

    [RelayCommand]
    private async Task StartEnrollmentAsync()
    {
        var result = await _enrollmentService.EnrollAsync(
            EnrollEmployeeCode,
            _device,
            _enroller,
            (i, total) => EnrollProgressMessage = $"Place your finger ({i} of {total})");

        StatusMessage = result.Message;
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        PunchPanelVisibility = Visibility.Visible;
        EnrollPanelVisibility = Visibility.Collapsed;
    }

    [RelayCommand]
    private void CancelEnrollment()
    {
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        PunchPanelVisibility = Visibility.Visible;
        EnrollPanelVisibility = Visibility.Collapsed;
    }
}
