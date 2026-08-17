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

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZakAttendanceAgent");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "agent.db");

        _host = HostComposition.CreateHostBuilder(dbPath).Build();

        // AddHostedService<SyncBackgroundService>() only actually runs its ExecuteAsync once the
        // host is started — without this, punches would queue locally forever and never sync.
        await _host.StartAsync();

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

        _appScope = _host.Services.CreateScope();
        _appScope.ServiceProvider.GetRequiredService<MainWindow>().Show();
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
        _host?.StopAsync().GetAwaiter().GetResult();
        _appScope?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
