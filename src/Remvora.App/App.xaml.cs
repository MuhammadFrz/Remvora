using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Remvora.App.ViewModels;
using Remvora.Application.Services;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Policies;
using Remvora.Infrastructure.Database;
using Remvora.Infrastructure.Repositories;
using Remvora.Windows.Msi;
using Remvora.Windows.Packages;
using Remvora.Windows.Registry;
using Remvora.Application.Workflows;

namespace Remvora.App;

/// <summary>
/// Main application entry and dependency injection container setup.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();

        // Run SQLite forward migrations on startup
        var migrator = Services.GetRequiredService<DatabaseMigrator>();
        migrator.Migrate();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Policies
        services.AddSingleton<IProtectedPathsPolicy, ProtectedPathsPolicy>();
        services.AddSingleton<IDeduplicationPolicy, DeduplicationPolicy>();
        services.AddSingleton<ICandidateScoringPolicy, CandidateScoringPolicy>();

        // Platform interop accessors
        services.AddSingleton<IRegistryAccessor, WindowsRegistryAccessor>();
        services.AddSingleton<IMsiAccessor, WindowsMsiAccessor>();
        services.AddSingleton<IPackageAccessor, WindowsPackageAccessor>();

        // Discovery Sources
        services.AddSingleton<IApplicationDiscoverySource, RegistryApplicationSource>();
        services.AddSingleton<IApplicationDiscoverySource, MsiApplicationSource>();
        services.AddSingleton<IApplicationDiscoverySource, PackageApplicationSource>();

        // Application Services
        services.AddSingleton<IApplicationDiscoveryService, ApplicationDiscoveryService>();
        services.AddSingleton<IUninstallOrchestrator, Remvora.Application.Workflows.UninstallOrchestrator>();

        // Leftover Scanners & Planner
        services.AddSingleton<Remvora.Application.Leftovers.ILeftoverScanner, Remvora.Application.Leftovers.CompositeLeftoverScanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ICleanupPlanner, Remvora.Application.Leftovers.CleanupPlanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ISubLeftoverScanner, Remvora.Windows.Leftovers.WindowsFileLeftoverScanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ISubLeftoverScanner, Remvora.Windows.Leftovers.WindowsRegistryLeftoverScanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ISubLeftoverScanner, Remvora.Windows.Leftovers.WindowsShortcutLeftoverScanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ISubLeftoverScanner, Remvora.Windows.Leftovers.WindowsServiceLeftoverScanner>();
        services.AddSingleton<Remvora.Application.Leftovers.ISubLeftoverScanner, Remvora.Windows.Leftovers.WindowsScheduledTaskLeftoverScanner>();

        // Processes & System Restore
        services.AddSingleton<Remvora.Application.Processes.IProcessDetector, Remvora.Windows.Processes.WindowsProcessDetector>();
        services.AddSingleton<Remvora.Application.RestorePoint.IRestorePointService, Remvora.Windows.RestorePoint.WindowsRestorePointService>();

        // Uninstall Strategies
        services.AddSingleton<Remvora.Application.Uninstall.IUninstallStrategy, Remvora.Windows.Uninstall.MsiUninstallStrategy>();
        services.AddSingleton<Remvora.Application.Uninstall.IUninstallStrategy, Remvora.Windows.Uninstall.RegistryCommandStrategy>();
        services.AddSingleton<Remvora.Application.Uninstall.IUninstallStrategy, Remvora.Windows.Uninstall.PackageUninstallStrategy>();

        // Persistence
        services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<IApplicationRepository, SqliteApplicationRepository>();
        services.AddSingleton<Remvora.Application.Auditing.IAuditLogRepository, SqliteAuditLogRepository>();

        // ViewModels
        services.AddTransient<AppsViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CleanupPreviewViewModel>();

        return services.BuildServiceProvider();
    }
}
