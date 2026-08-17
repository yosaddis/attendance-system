using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using AttendanceAgent.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AttendanceAgent;

/// <summary>
/// Central place for the agent's DI registrations. Extracted out of App.xaml.cs so the
/// composition can be built and validated (ValidateOnBuild/ValidateScopes) from a test without
/// needing a real WPF Application.
/// </summary>
public static class HostComposition
{
    public static IHostBuilder CreateHostBuilder(string dbPath) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services => ConfigureServices(services, dbPath));

    public static void ConfigureServices(IServiceCollection services, string dbPath)
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

        services.AddHostedService<SyncBackgroundService>();
    }
}
