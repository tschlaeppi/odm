namespace Odm.SharedKernel.Persistence;

public enum DatabaseProvider
{
    Postgres,
    SqlServer
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    public string ConnectionString { get; set; } = "";
}
