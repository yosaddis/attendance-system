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
        var vm = new MainViewModel(capture, new FakeFingerprintDevice());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("", vm.EmployeeCode);
        Assert.Equal("Punch recorded for Jane Doe.", vm.StatusMessage);
    }

    [Fact]
    public async Task Punch_OnFailure_KeepsEmployeeCodeAndSetsStatus()
    {
        var capture = new FakeCaptureService { Result = new PunchResult(false, "Fingerprint did not match.") };
        var vm = new MainViewModel(capture, new FakeFingerprintDevice());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("E001", vm.EmployeeCode);
        Assert.Equal("Fingerprint did not match.", vm.StatusMessage);
    }
}
