using AttendanceAgent.Devices;
using Xunit;

namespace AttendanceAgent.Tests;

public class DeviceCaptureTests
{
    [Fact]
    public void CaptureOnce_ReturnsCapturedBytes_AndReleases()
    {
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9, 9 } };

        var result = DeviceCapture.CaptureOnce(device);

        Assert.Equal(new byte[] { 9, 9, 9 }, result);
        Assert.False(device.IsAcquired);
        Assert.Equal(new[] { "Acquire", "Capture", "Release" }, device.CallLog);
    }

    [Fact]
    public void CaptureOnce_WhenCaptureThrows_StillReleases()
    {
        var device = new FakeFingerprintDevice { ThrowOnCapture = true };

        Assert.Throws<InvalidOperationException>(() => DeviceCapture.CaptureOnce(device));

        Assert.False(device.IsAcquired);
        Assert.Equal(new[] { "Acquire", "Capture", "Release" }, device.CallLog);
    }
}
