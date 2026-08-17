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
                // WPF pack-URI resource resolution (used by MainWindow's InitializeComponent) needs
                // an Application instance to exist on the thread.
                if (System.Windows.Application.Current is null)
                {
                    new System.Windows.Application();
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
}
