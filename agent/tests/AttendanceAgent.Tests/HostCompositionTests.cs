using System.Threading;
using AttendanceAgent.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AttendanceAgent.Tests;

/// <summary>
/// Regression coverage for the "Singleton depending on Scoped services" captive-dependency bug:
/// MainViewModel/MainWindow were originally registered AddSingleton while transitively depending
/// on the Scoped AgentDbContext (via IPunchCaptureService -> ... -> AgentDbContext). That throws
/// at Build() time once ValidateOnBuild/ValidateScopes is on (as it is by default in the
/// Development environment) and silently captures a process-lifetime DbContext otherwise.
///
/// This builds the exact same registrations App.xaml.cs uses (via HostComposition) with
/// ValidateScopes/ValidateOnBuild forced on regardless of the ambient environment, and confirms
/// the container can still be built and MainWindow/MainViewModel resolved from a scope.
///
/// MainWindow is a real WPF Window, so this must run on an STA thread the way the real app does.
/// </summary>
public class HostCompositionTests
{
    [Fact]
    public void Build_And_Resolve_MainWindow_And_MainViewModel_From_A_Scope_Succeeds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // MainWindow's InitializeComponent resolves StaticResource brushes/fonts that live in
                // Styles.xaml, which only gets merged into Application.Resources via App's own
                // generated InitializeComponent() (the same call the real Main() makes before
                // app.Run()). A bare `new Application()` skips that merge, so this must construct the
                // real App type — not the base class — or StaticResource lookups in MainWindow.xaml
                // fail here even though they work at runtime.
                if (System.Windows.Application.Current is null)
                {
                    new App().InitializeComponent();
                }

                var dbPath = Path.Combine(Path.GetTempPath(), $"agent-host-composition-test-{Guid.NewGuid()}.db");
                try
                {
                    using var host = HostComposition.CreateHostBuilder(dbPath)
                        .UseDefaultServiceProvider(options =>
                        {
                            options.ValidateScopes = true;
                            options.ValidateOnBuild = true;
                        })
                        .Build();

                    using var scope = host.Services.CreateScope();
                    var viewModel = scope.ServiceProvider.GetRequiredService<MainViewModel>();
                    var window = scope.ServiceProvider.GetRequiredService<MainWindow>();

                    Assert.NotNull(viewModel);
                    Assert.NotNull(window);
                }
                finally
                {
                    if (File.Exists(dbPath)) File.Delete(dbPath);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    /// <summary>
    /// The fingerprint vendor is a build-time choice (the DeviceVendor MSBuild property), so a
    /// deployed AttendanceAgent.exe gives no runtime indication of which SDK it will talk to.
    /// App.xaml.cs logs <see cref="HostComposition.CompiledDeviceVendor"/> once at startup so that
    /// question is answerable from a running station's own log; this asserts the value is actually
    /// populated and reports the build kind, rather than being an empty or placeholder string.
    ///
    /// The test assembly is built Debug in the normal `dotnet test` run, which is the configuration
    /// that registers the fakes — so that is the branch asserted here. The Release branches are
    /// covered by the vendor-registration tests plus the build-level DeviceVendor validation in
    /// AttendanceAgent.csproj.
    /// </summary>
    [Fact]
    public void CompiledDeviceVendor_IdentifiesTheVendorAndBuildKind()
    {
        var vendor = HostComposition.CompiledDeviceVendor;

        Assert.False(string.IsNullOrWhiteSpace(vendor));
#if DEBUG
        Assert.Contains("Debug build", vendor);
        Assert.Contains("Fake", vendor);
#elif DEVICE_VENDOR_ZK4500
        Assert.Contains("Zk4500", vendor);
#else
        Assert.Contains("SecuGen", vendor);
#endif
    }

    [Fact]
    public void CompiledDeviceVendor_MentionsTheEnroller()
    {
        var vendor = HostComposition.CompiledDeviceVendor;

#if DEBUG
        Assert.Contains("FakeFingerprintEnroller", vendor);
#elif DEVICE_VENDOR_ZK4500
        Assert.Contains("ZkFingerprintEnroller", vendor);
#else
        Assert.Contains("SecuGenFingerprintEnroller", vendor);
#endif
    }
}
