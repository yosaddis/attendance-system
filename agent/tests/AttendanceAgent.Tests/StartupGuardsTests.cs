using AttendanceAgent.Devices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceAgent.Tests;

/// <summary>
/// Regression coverage for the "ship the fake fingerprint device/verifier by accident" bug:
/// FakeFingerprintVerifier.AlwaysMatches defaults to true, so if HostComposition's fake
/// registrations (meant only for development/testing) were ever left in place in a Release build,
/// ANY captured fingerprint would be accepted as a match for ANY enrolled template — a complete
/// authentication bypass. AssertNoFakeHardwareInRelease is the fail-fast guard against that.
/// </summary>
public class StartupGuardsTests
{
    private static ServiceProvider BuildProviderWithFakes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFingerprintDevice, FakeFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, FakeFingerprintVerifier>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AssertNoFakeHardwareInRelease_FakesRegistered_IsReleaseBuild_Throws()
    {
        using var provider = BuildProviderWithFakes();

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupGuards.AssertNoFakeHardwareInRelease(provider, isReleaseBuild: true));

        Assert.Contains("FakeFingerprintDevice", ex.Message);
        Assert.Contains("Release build", ex.Message);
    }

    [Fact]
    public void AssertNoFakeHardwareInRelease_FakesRegistered_NotReleaseBuild_DoesNotThrow()
    {
        using var provider = BuildProviderWithFakes();

        // Debug/development builds are expected to use the fakes — no real hardware is required
        // to run the app during development.
        StartupGuards.AssertNoFakeHardwareInRelease(provider, isReleaseBuild: false);
    }

    [Fact]
    public void AssertNoFakeHardwareInRelease_RealImplementationsRegistered_IsReleaseBuild_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFingerprintDevice, RealFingerprintDeviceStub>();
        services.AddSingleton<IFingerprintVerifier, RealFingerprintVerifierStub>();
        using var provider = services.BuildServiceProvider();

        StartupGuards.AssertNoFakeHardwareInRelease(provider, isReleaseBuild: true);
    }

    /// <summary>Stand-ins for "a real hardware SDK implementation" — anything that isn't the Fake* types.</summary>
    private sealed class RealFingerprintDeviceStub : IFingerprintDevice
    {
        public void Acquire() { }
        public byte[] Capture() => Array.Empty<byte>();
        public void Release() { }
    }

    private sealed class RealFingerprintVerifierStub : IFingerprintVerifier
    {
        public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate) => false;
    }
}
