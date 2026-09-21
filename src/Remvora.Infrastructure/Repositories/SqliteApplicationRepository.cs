using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Signatures;
using Remvora.Infrastructure.Database;

namespace Remvora.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed persistence repository for canonical application inventory records.
/// </summary>
public sealed class SqliteApplicationRepository : IApplicationRepository
{
    private const string SelectColumnsSql = """
        SELECT
            id AS Id,
            display_name AS DisplayName,
            publisher AS Publisher,
            display_version AS DisplayVersion,
            install_date AS InstallDate,
            install_location AS InstallLocation,
            estimated_size_bytes AS EstimatedSizeBytes,
            calculated_size_bytes AS CalculatedSizeBytes,
            scope AS Scope,
            architecture AS Architecture,
            installer_type AS InstallerType,
            product_code AS ProductCode,
            package_family_name AS PackageFamilyName,
            registry_key_path AS RegistryKeyPath,
            uninstall_string AS UninstallString,
            quiet_uninstall_string AS QuietUninstallString,
            modify_path AS ModifyPath,
            is_msi AS IsMsi,
            requires_elevation AS RequiresElevation,
            is_system_component AS IsSystemComponent,
            running_state AS RunningState,
            signature_status AS SignatureStatus,
            signature_publisher AS SignaturePublisher,
            last_discovered AS LastDiscovered,
            discovery_sources_json AS DiscoverySourcesJson
        FROM applications
        """;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteApplicationRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqliteApplicationRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SqliteApplicationRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = $"{SelectColumnsSql} ORDER BY display_name COLLATE NOCASE;";

        var rows = await connection.QueryAsync<ApplicationRow>(sql).ConfigureAwait(false);
        var list = new List<ApplicationRecord>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(MapToRecord(row));
        }

        return list;
    }

    public async Task<ApplicationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = $"{SelectColumnsSql} WHERE id = @Id;";

        var row = await connection.QuerySingleOrDefaultAsync<ApplicationRow>(sql, new { Id = id.ToString() }).ConfigureAwait(false);
        return row == null ? null : MapToRecord(row);
    }

    public async Task SaveAsync(IEnumerable<ApplicationRecord> applications, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applications);

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        const string upsertSql = """
            INSERT INTO applications (
                id, display_name, publisher, display_version, install_date, install_location,
                estimated_size_bytes, calculated_size_bytes, scope, architecture, installer_type,
                product_code, package_family_name, registry_key_path, uninstall_string,
                quiet_uninstall_string, modify_path, is_msi, requires_elevation, is_system_component,
                running_state, signature_status, signature_publisher, last_discovered,
                discovery_sources_json
            ) VALUES (
                @Id, @DisplayName, @Publisher, @DisplayVersion, @InstallDate, @InstallLocation,
                @EstimatedSizeBytes, @CalculatedSizeBytes, @Scope, @Architecture, @InstallerType,
                @ProductCode, @PackageFamilyName, @RegistryKeyPath, @UninstallString,
                @QuietUninstallString, @ModifyPath, @IsMsi, @RequiresElevation, @IsSystemComponent,
                @RunningState, @SignatureStatus, @SignaturePublisher, @LastDiscovered,
                @DiscoverySourcesJson
            )
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                publisher = excluded.publisher,
                display_version = excluded.display_version,
                install_date = excluded.install_date,
                install_location = excluded.install_location,
                estimated_size_bytes = excluded.estimated_size_bytes,
                calculated_size_bytes = excluded.calculated_size_bytes,
                scope = excluded.scope,
                architecture = excluded.architecture,
                installer_type = excluded.installer_type,
                product_code = excluded.product_code,
                package_family_name = excluded.package_family_name,
                registry_key_path = excluded.registry_key_path,
                uninstall_string = excluded.uninstall_string,
                quiet_uninstall_string = excluded.quiet_uninstall_string,
                modify_path = excluded.modify_path,
                is_msi = excluded.is_msi,
                requires_elevation = excluded.requires_elevation,
                is_system_component = excluded.is_system_component,
                running_state = excluded.running_state,
                signature_status = excluded.signature_status,
                signature_publisher = excluded.signature_publisher,
                last_discovered = excluded.last_discovered,
                discovery_sources_json = excluded.discovery_sources_json;
            """;

        var rows = applications.Select(MapToRow).ToList();
        await connection.ExecuteAsync(upsertSql, rows, transaction).ConfigureAwait(false);
        transaction.Commit();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "DELETE FROM applications WHERE id = @Id;";
        await connection.ExecuteAsync(sql, new { Id = id.ToString() }).ConfigureAwait(false);
    }

    private static ApplicationRow MapToRow(ApplicationRecord app)
    {
        return new ApplicationRow
        {
            Id = app.Id.ToString(),
            DisplayName = app.DisplayName,
            Publisher = app.Publisher,
            DisplayVersion = app.DisplayVersion,
            InstallDate = app.InstallDate?.ToString("o"),
            InstallLocation = app.InstallLocation,
            EstimatedSizeBytes = app.EstimatedSizeBytes,
            CalculatedSizeBytes = app.CalculatedSizeBytes,
            Scope = (int)app.Scope,
            Architecture = (int)app.Architecture,
            InstallerType = (int)app.InstallerType,
            ProductCode = app.Identity.ProductCode,
            PackageFamilyName = app.Identity.PackageFamilyName,
            RegistryKeyPath = app.Identity.RegistryKeyPath,
            UninstallString = app.Uninstall.UninstallString,
            QuietUninstallString = app.Uninstall.QuietUninstallString,
            ModifyPath = app.Uninstall.ModifyPath,
            IsMsi = app.Uninstall.IsMsi ? 1 : 0,
            RequiresElevation = app.Uninstall.RequiresElevation ? 1 : 0,
            IsSystemComponent = app.IsSystemComponent ? 1 : 0,
            RunningState = (int)app.RunningState,
            SignatureStatus = (int)(app.Signature?.Status ?? SignatureStatus.Unknown),
            SignaturePublisher = app.Signature?.Publisher,
            LastDiscovered = app.LastDiscovered.ToString("o"),
            DiscoverySourcesJson = JsonSerializer.Serialize(app.DiscoverySources, JsonOptions)
        };
    }

    private static ApplicationRecord MapToRecord(ApplicationRow row)
    {
        var identity = new ApplicationIdentity(
            row.DisplayName,
            row.Publisher,
            row.DisplayVersion,
            row.InstallLocation,
            row.ProductCode,
            row.PackageFamilyName,
            row.RegistryKeyPath);

        var uninstall = new UninstallInfo(
            row.UninstallString,
            row.QuietUninstallString,
            row.ModifyPath,
            row.IsMsi == 1,
            row.RequiresElevation == 1);

        DateTimeOffset? installDate = null;
        if (!string.IsNullOrWhiteSpace(row.InstallDate) && DateTimeOffset.TryParse(row.InstallDate, out var dt))
            installDate = dt;

        DateTimeOffset lastDiscovered = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(row.LastDiscovered) && DateTimeOffset.TryParse(row.LastDiscovered, out var ld))
            lastDiscovered = ld;

        IReadOnlyList<DiscoverySource>? discoverySources = null;
        if (!string.IsNullOrWhiteSpace(row.DiscoverySourcesJson))
        {
            try
            {
                discoverySources = JsonSerializer.Deserialize<List<DiscoverySource>>(row.DiscoverySourcesJson, JsonOptions);
            }
            catch
            {
                discoverySources = [];
            }
        }

        SignatureInfo? signature = null;
        if (row.SignatureStatus != (int)SignatureStatus.Unknown || !string.IsNullOrWhiteSpace(row.SignaturePublisher))
        {
            signature = new SignatureInfo((SignatureStatus)row.SignatureStatus, row.SignaturePublisher);
        }

        return new ApplicationRecord(
            Guid.Parse(row.Id),
            row.DisplayName,
            row.Publisher,
            row.DisplayVersion,
            installDate,
            row.InstallLocation,
            row.EstimatedSizeBytes,
            row.CalculatedSizeBytes,
            (InstallationScope)row.Scope,
            (ArchitectureType)row.Architecture,
            (InstallerType)row.InstallerType,
            identity,
            uninstall,
            discoverySources,
            signature,
            (RunningStatus)row.RunningState,
            row.IsSystemComponent == 1,
            lastDiscovered);
    }

    private sealed class ApplicationRow
    {
        public required string Id { get; set; }
        public required string DisplayName { get; set; }
        public string? Publisher { get; set; }
        public string? DisplayVersion { get; set; }
        public string? InstallDate { get; set; }
        public string? InstallLocation { get; set; }
        public long? EstimatedSizeBytes { get; set; }
        public long? CalculatedSizeBytes { get; set; }
        public int Scope { get; set; }
        public int Architecture { get; set; }
        public int InstallerType { get; set; }
        public string? ProductCode { get; set; }
        public string? PackageFamilyName { get; set; }
        public string? RegistryKeyPath { get; set; }
        public string? UninstallString { get; set; }
        public string? QuietUninstallString { get; set; }
        public string? ModifyPath { get; set; }
        public int IsMsi { get; set; }
        public int RequiresElevation { get; set; }
        public int IsSystemComponent { get; set; }
        public int RunningState { get; set; }
        public int SignatureStatus { get; set; }
        public string? SignaturePublisher { get; set; }
        public required string LastDiscovered { get; set; }
        public required string DiscoverySourcesJson { get; set; }
    }
}
