using AttendanceAgent.Devices.Zk;
using Xunit;

namespace AttendanceAgent.Tests;

public class ZkFingerprintDeviceRegistrationTests
{
    [Fact]
    public void ZkFingerprintDevice_ImplementsIFingerprintDevice()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintDevice>(
            (object)Activator.CreateInstance(typeof(ZkFingerprintDevice))!);
    }

    [Fact]
    public void ZkFingerprintVerifier_ImplementsIFingerprintVerifier()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintVerifier>(
            (object)Activator.CreateInstance(typeof(ZkFingerprintVerifier))!);
    }
}
