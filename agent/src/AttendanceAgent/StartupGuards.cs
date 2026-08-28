using AttendanceAgent.Devices;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceAgent;

/// <summary>
/// Fail-fast checks run once at startup, kept separate from App.xaml.cs so they can be exercised
/// from a test without needing a real WPF Application.
/// </summary>
public static class StartupGuards
{
    /// <summary>
    /// Throws if any fake (non-hardware) fingerprint device/verifier/enroller are still the
    /// registered DI implementations while running as a Release build. FakeFingerprintVerifier.
    /// AlwaysMatches defaults to true, so it accepts ANY captured "fingerprint" (including from
    /// FakeFingerprintDevice, which never talks to real hardware) as a match for ANY enrolled
    /// template. If a build with the fakes still wired in were ever deployed to a customer site,
    /// anyone who knows a coworker's employee code could punch in as them — a complete
    /// authentication bypass in a payroll/attendance system. FakeFingerprintEnroller's own
    /// default MergedResult (an empty byte array) would additionally let anyone silently
    /// overwrite a real employee's template with an empty one. This check is deliberately loud
    /// (it throws, it doesn't silently substitute anything) so none of the three can ever be
    /// accidentally shipped.
    ///
    /// <paramref name="isReleaseBuild"/> is passed in by the caller (App.xaml.cs supplies the
    /// compile-time `#if DEBUG`/`#else` value) rather than this method checking a compilation
    /// symbol itself, so the guard's logic can be unit-tested under both branches regardless of
    /// which configuration the test assembly itself was built in.
    /// </summary>
    public static void AssertNoFakeHardwareInRelease(IServiceProvider services, bool isReleaseBuild)
    {
        if (!isReleaseBuild) return;

        var device = services.GetRequiredService<IFingerprintDevice>();
        var verifier = services.GetRequiredService<IFingerprintVerifier>();
        var enroller = services.GetRequiredService<IFingerprintEnroller>();

        if (device is FakeFingerprintDevice || verifier is FakeFingerprintVerifier || enroller is FakeFingerprintEnroller)
        {
            throw new InvalidOperationException(
                "FakeFingerprintDevice/FakeFingerprintVerifier/FakeFingerprintEnroller must not be used in a Release build — " +
                "replace with real hardware SDK implementations before shipping.");
        }
    }
}
