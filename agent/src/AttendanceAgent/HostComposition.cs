using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using AttendanceAgent.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent;

/// <summary>
/// Central place for the agent's DI registrations. Extracted out of App.xaml.cs so the
/// composition can be built and validated (ValidateOnBuild/ValidateScopes) from a test without
/// needing a real WPF Application.
/// </summary>
public static class HostComposition
{
    public static IHostBuilder CreateHostBuilder(string dbPath, TimeSpan? syncInterval = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services => ConfigureServices(services, dbPath, syncInterval));

    /// <param name="syncInterval">
    /// Passed straight through to SyncBackgroundService's constructor; null (the default, used by
    /// the real App.xaml.cs) keeps SyncBackgroundService's own 30-second default. Only exists so
    /// tests can build the exact same composition App.xaml.cs uses but with a short interval, so
    /// the sync loop can be observed making several passes within a test's lifetime.
    /// </param>
    public static void ConfigureServices(IServiceCollection services, string dbPath, TimeSpan? syncInterval = null)
    {
        services.AddDbContext<AgentDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddHttpClient<IBackendApiClient, BackendApiClient>();
        services.AddScoped<IEmployeeDirectoryService, EmployeeDirectoryService>();
        services.AddScoped<ITemplateCacheService, TemplateCacheService>();
        services.AddScoped<IPunchQueueService, PunchQueueService>();
        services.AddScoped<IPunchCaptureService, PunchCaptureService>();
        services.AddSingleton<IFingerprintDevice, FakeFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, FakeFingerprintVerifier>();

        // MainViewModel/MainWindow are Scoped (not Singleton) because they transitively depend on
        // the Scoped AgentDbContext (via IPunchCaptureService -> IEmployeeDirectoryService /
        // ITemplateCacheService / IPunchQueueService). A Singleton consuming a Scoped service is a
        // captive-dependency bug: Host.CreateDefaultBuilder() enables ValidateOnBuild/ValidateScopes
        // in the Development environment, so it would throw at .Build() time there; in Production
        // (the default when DOTNET_ENVIRONMENT is unset) it would instead silently resolve the
        // Scoped AgentDbContext from the root container, giving the UI a single DbContext instance
        // that lives for the whole process — unbounded EF Core change-tracker growth on a kiosk app
        // meant to run for weeks. Keeping the whole chain Scoped and using one app-lifetime
        // IServiceScope (see App.xaml.cs) gives the desired "one instance for the app's lifetime"
        // behavior without violating DI's scope rules.
        services.AddScoped<MainViewModel>();
        services.AddScoped<MainWindow>();

        services.AddHostedService(sp => new SyncBackgroundService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<SyncBackgroundService>>(),
            syncInterval));
    }

    /// <summary>
    /// Starts <paramref name="host"/> the way App.xaml.cs's OnStartup does — via Task.Run rather
    /// than a direct `await host.StartAsync()`.
    ///
    /// SynchronizationContext.Current is a plain [ThreadStatic] on the CLR — it never flows across
    /// a Task.Run boundary onto a thread-pool thread. StartAsync synchronously kicks off
    /// BackgroundService.ExecuteAsync up to its first await, so running StartAsync itself via
    /// Task.Run guarantees ExecuteAsync's subsequent awaits (Task.Delay, EF queries, HTTP calls —
    /// see SyncBackgroundService) capture a *null* ambient SynchronizationContext, not the caller's.
    /// Awaiting `host.StartAsync()` directly on a thread that has one installed (e.g. WPF's
    /// DispatcherSynchronizationContext) would instead capture it, and every sync-loop iteration
    /// would try to marshal its continuation back to the UI thread — serializing/deadlocking the
    /// "background" work against the UI thread and against OnExit's bounded StopAsync wait.
    ///
    /// Extracted here (rather than left inlined in OnStartup) so a test can call this exact helper
    /// against a real host and prove the mechanism holds, instead of hand-rolling a copy of
    /// `Task.Run(() => host.StartAsync())` that could silently drift from what App.xaml.cs actually
    /// calls — see SyncBackgroundServiceThreadingTests.
    /// </summary>
    public static Task StartHostAsync(IHost host) => Task.Run(() => host.StartAsync());
}
