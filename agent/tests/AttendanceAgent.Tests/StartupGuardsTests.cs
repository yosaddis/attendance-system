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
    /// <summary>
    /// The tests below (BuildProviderWithFakes-based) only prove the guard's own logic is correct
    /// against a hand-built, minimal ServiceCollection that registers exactly the two services the
    /// guard inspects -- they say nothing about whether the guard, run against the DI graph
    /// App.xaml.cs actually ships (HostComposition.CreateHostBuilder, with all of its
    /// registrations -- AddHttpClient, EF Core, the hosted SyncBackgroundService, etc.), resolves
    /// IFingerprintDevice/IFingerprintVerifier the way this build configuration intends. A change to
    /// HostComposition that altered how those two services are registered/resolved (a different
    /// lifetime, a decorator, a factory wrapping the fake in another type) could pass every test
    /// above while the real composition silently drifted from what it's supposed to wire up.
    ///
    /// This test closes that gap: it builds the host through HostComposition.CreateHostBuilder --
    /// the exact same composition App.xaml.cs uses -- and calls AssertNoFakeHardwareInRelease
    /// against its real, fully-built IServiceProvider with isReleaseBuild: true. This is the guard
    /// wired to what actually ships, not to a stand-in.
    ///
    /// The expected outcome flips with the build configuration this test assembly (and, via
    /// ProjectReference, AttendanceAgent itself) is compiled under -- deliberately, and by design:
    /// before Task 10 (real SecuGen integration), HostComposition had nothing but fakes to
    /// register, so a genuine Release build would ALWAYS fail this guard -- exactly the gap Task 10
    /// closes. Compiled Debug, HostComposition still wires up the fakes (see its #if DEBUG /
    /// #else), so the guard must still throw here. Compiled Release, HostComposition now wires up
    /// SecuGenFingerprintDevice/SecuGenFingerprintVerifier, so the guard must NOT throw -- asserting
    /// otherwise here would be re-encoding the pre-Task-10 bug as a requirement.
    /// </summary>
    [Fact]
    public void AssertNoFakeHardwareInRelease_AgainstRealHostComposition_IsReleaseBuild()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agent-startup-guard-real-composition-test-{Guid.NewGuid()}.db");
        try
        {
            using var host = HostComposition.CreateHostBuilder(dbPath).Build();

#if DEBUG
            var ex = Assert.Throws<InvalidOperationException>(
                () => StartupGuards.AssertNoFakeHardwareInRelease(host.Services, isReleaseBuild: true));

            Assert.Contains("FakeFingerprintDevice", ex.Message);
            Assert.Contains("Release build", ex.Message);
#else
            StartupGuards.AssertNoFakeHardwareInRelease(host.Services, isReleaseBuild: true);
#endif
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

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
        public byte[] CaptureForEnrollment() => Array.Empty<byte>();
        public void Release() { }
    }

    private sealed class RealFingerprintVerifierStub : IFingerprintVerifier
    {
        public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate) => false;
    }
}
