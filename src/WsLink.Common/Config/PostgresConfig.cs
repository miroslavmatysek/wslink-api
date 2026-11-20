namespace WsLink.Common.Config;

public class PostgresConfig
{
    public const string ConfigSectionName = "Postgres";

    /// <summary>
    /// Npgsql connection string, e.g. Host=postgres;Database=wslink;Username=wslink;Password=...
    /// The table is created by scripts/database/timescaledb.sql, not by the service.
    /// </summary>
    public string? ConnectionString { get; set; }

    public bool Enabled { get; set; } = false;
}
