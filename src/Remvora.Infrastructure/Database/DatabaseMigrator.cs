using Dapper;

namespace Remvora.Infrastructure.Database;

/// <summary>
/// Handles database schema initialization and forward migrations.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseMigrator(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public void Migrate()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string createApplicationsTableSql = """
            CREATE TABLE IF NOT EXISTS applications (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                publisher TEXT,
                display_version TEXT,
                install_date TEXT,
                install_location TEXT,
                estimated_size_bytes INTEGER,
                calculated_size_bytes INTEGER,
                scope INTEGER NOT NULL,
                architecture INTEGER NOT NULL,
                installer_type INTEGER NOT NULL,
                product_code TEXT,
                package_family_name TEXT,
                registry_key_path TEXT,
                uninstall_string TEXT,
                quiet_uninstall_string TEXT,
                modify_path TEXT,
                is_msi INTEGER NOT NULL,
                requires_elevation INTEGER NOT NULL,
                is_system_component INTEGER NOT NULL,
                running_state INTEGER NOT NULL,
                signature_status INTEGER NOT NULL,
                signature_publisher TEXT,
                last_discovered TEXT NOT NULL,
                discovery_sources_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_applications_name ON applications(display_name);
            CREATE INDEX IF NOT EXISTS idx_applications_product_code ON applications(product_code);
            CREATE INDEX IF NOT EXISTS idx_applications_package_family ON applications(package_family_name);
            CREATE INDEX IF NOT EXISTS idx_applications_registry_key ON applications(registry_key_path);

            CREATE TABLE IF NOT EXISTS audit_events (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                category INTEGER NOT NULL,
                severity INTEGER NOT NULL,
                action TEXT NOT NULL,
                target TEXT,
                application_id TEXT,
                transaction_id TEXT,
                result TEXT,
                error_code INTEGER NOT NULL,
                details TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_audit_events_timestamp ON audit_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_audit_events_app_id ON audit_events(application_id);
            CREATE INDEX IF NOT EXISTS idx_audit_events_trans_id ON audit_events(transaction_id);

            CREATE TABLE IF NOT EXISTS transactions (
                id TEXT PRIMARY KEY,
                plan_id TEXT NOT NULL,
                application_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                phase INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                restore_point_sequence INTEGER,
                journal_path TEXT,
                summary_notes TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_transactions_app_id ON transactions(application_id);
            CREATE INDEX IF NOT EXISTS idx_transactions_started_at ON transactions(started_at);

            CREATE TABLE IF NOT EXISTS transaction_items (
                id TEXT PRIMARY KEY,
                transaction_id TEXT NOT NULL,
                item_type INTEGER NOT NULL,
                target_location TEXT NOT NULL,
                original_state TEXT,
                backup_path TEXT,
                result INTEGER NOT NULL,
                is_reversible INTEGER NOT NULL,
                error_message TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_transaction_items_trans_id ON transaction_items(transaction_id);
            """;

        connection.Execute(createApplicationsTableSql);
    }
}
