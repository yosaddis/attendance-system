using System.IO;
using System.Windows;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent;

public partial class App : Application
{
    private IHost? _host;

    // One scope for the whole app's lifetime — the desktop-app equivalent of ASP.NET Core's
    // per-request scope. MainWindow/MainViewModel (and everything they pull in) are resolved from
    // this single scope so they get one shared AgentDbContext instance for as long as the app
    // runs, without registering them as DI Singletons (which would violate DI's scope rules — see
    // the comment in HostComposition.ConfigureServices).
    private IServiceScope? _appScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Last-resort safety net for anything that slips past the offline-handling catches in
        // EmployeeDirectoryService/TemplateCacheService/SyncBackgroundService — NOT the primary
        // mechanism for handling backend-unreachable failures (those are handled at the source).
        // This exists purely to keep the kiosk process alive instead of crashing silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Everything below this point (host build, EnsureCreated against agent.db, first-run
        // settings prompt, host start, MainWindow.Show()) runs BEFORE any window exists. Without
        // this try/catch, a failure here (e.g. EnsureCreated() against a locked/corrupt/read-only
        // agent.db, HostComposition.Build() failing validation, or MainWindow's InitializeComponent
        // throwing) would still end up reported through DispatcherUnhandledException below — but
        // that handler's `e.Handled = true` would leave a running process with zero windows, which
        // under the default ShutdownMode (OnLastWindowClose) never exits: an invisible zombie
        // process. Catching here guarantees we always reach Shutdown(1) ourselves instead of
        // relying on that fallback for a failure this early.
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZakAttendanceAgent");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "agent.db");

            _host = HostComposition.CreateHostBuilder(dbPath).Build();

            // Fails fast (Release builds only) if the fake, non-hardware fingerprint device/verifier
            // are still the registered implementations — see StartupGuards for why shipping them
            // unmodified would be a silent authentication bypass.
#if DEBUG
            StartupGuards.AssertNoFakeHardwareInRelease(_host.Services, isReleaseBuild: false);
#else
            StartupGuards.AssertNoFakeHardwareInRelease(_host.Services, isReleaseBuild: true);
#endif

            // The database schema and first-run settings MUST exist before the host is started:
            // AddHostedService<SyncBackgroundService>() runs its ExecuteAsync as soon as the host
            // starts, and it immediately queries QueuedPunches. On a fresh install (no agent.db yet)
            // that would throw "no such table: QueuedPunches" before EnsureCreated() ever ran.
            using (var scope = _host.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
            }

            using (var scope = _host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                if (!db.Settings.Any())
                {
                    var backendUrl = Microsoft.VisualBasic.Interaction.InputBox(
                        "Backend base URL (e.g. https://attendance.example.com):", "First-run setup");
                    var apiKey = Microsoft.VisualBasic.Interaction.InputBox(
                        "Station API key (from the admin portal's station creation screen):", "First-run setup");
                    db.Settings.Add(new AgentSettings { BackendBaseUrl = backendUrl, StationApiKey = apiKey });
                    db.SaveChanges();
                }
            }

            // AddHostedService<SyncBackgroundService>() only actually runs its ExecuteAsync once the
            // host is started — without this, punches would queue locally forever and never sync.
            //
            // Delegates to HostComposition.StartHostAsync (rather than inlining Task.Run here) so
            // SyncBackgroundServiceThreadingTests can call the exact same helper this line calls —
            // see that helper's XML doc for why Task.Run (vs. a direct await) matters: awaiting on
            // the WPF UI thread would capture the active DispatcherSynchronizationContext, serializing
            // the "background" sync loop onto the UI thread and racing OnExit's bounded StopAsync
            // wait below.
            await HostComposition.StartHostAsync(_host!);

            _appScope = _host.Services.CreateScope();
            _appScope.ServiceProvider.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            HandleStartupFailure(ex);
        }
    }

    /// <summary>
    /// Reached whenever anything in OnStartup's body throws before MainWindow.Show() runs. Logs if
    /// a logger happens to be available yet (it may not be — the host might have failed to build at
    /// all), always writes to Debug output as a fallback, shows the user a message, then forces the
    /// process to exit. Without the explicit Shutdown(1) call, a failure here would otherwise leave
    /// a windowless process running forever under the default ShutdownMode.
    /// </summary>
    private void HandleStartupFailure(Exception ex)
    {
        try
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(ex, "Startup failed before the main window could be shown; exiting.");
        }
        catch
        {
            // The logger itself may be unavailable this early (e.g. the host never finished
            // building) — fall through to the Debug/MessageBox reporting below regardless.
        }

        System.Diagnostics.Debug.WriteLine($"FATAL: Attendance Agent failed to start: {ex}");

        MessageBox.Show(
            $"Attendance Agent failed to start and will now exit:\n\n{ex.Message}",
            "Attendance Agent - Startup Failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(1);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled exception reached the WPF dispatcher.");
        MessageBox.Show(
            $"An unexpected error occurred and has been logged:\n\n{e.Exception.Message}",
            "Attendance Agent",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Give the background service (sync loop) a chance to stop cleanly before disposing.
        // Blocking here (rather than awaiting) guarantees this completes before the process
        // continues tearing down, which an `async void OnExit` could not guarantee.
        //
        // Bounded by a timeout, and Dispose() is guaranteed via `finally` no matter what happens
        // to StopAsync — it must run whether StopAsync completes, times out, or throws, otherwise
        // _appScope/_host are leaked and the process may not exit cleanly.
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _host?.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, "Host failed to stop cleanly within the shutdown timeout.");
        }
        finally
        {
            _appScope?.Dispose();
            _host?.Dispose();
        }

        base.OnExit(e);
    }
}
