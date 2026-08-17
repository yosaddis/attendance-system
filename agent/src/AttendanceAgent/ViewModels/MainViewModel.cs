using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AttendanceAgent.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPunchCaptureService _captureService;
    private readonly IFingerprintDevice _device;

    [ObservableProperty]
    private string employeeCode = "";

    [ObservableProperty]
    private string statusMessage = "";

    public MainViewModel(IPunchCaptureService captureService, IFingerprintDevice device)
    {
        _captureService = captureService;
        _device = device;
    }

    [RelayCommand]
    private async Task PunchAsync(string punchType)
    {
        var result = await _captureService.CapturePunchAsync(EmployeeCode, punchType, _device);
        StatusMessage = result.Message;
        if (result.Success) EmployeeCode = "";
    }
}
