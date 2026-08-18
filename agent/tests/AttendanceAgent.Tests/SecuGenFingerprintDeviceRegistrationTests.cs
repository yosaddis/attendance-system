using AttendanceAgent.Devices.SecuGen;
using Xunit;

namespace AttendanceAgent.Tests;

public class SecuGenFingerprintDeviceRegistrationTests
{
    [Fact]
    public void SecuGenFingerprintDevice_ImplementsIFingerprintDevice()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintDevice>(
            (object)Activator.CreateInstance(typeof(SecuGenFingerprintDevice))!);
    }

    [Fact]
    public void SecuGenFingerprintVerifier_ImplementsIFingerprintVerifier()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintVerifier>(
            (object)Activator.CreateInstance(typeof(SecuGenFingerprintVerifier))!);
    }
}
