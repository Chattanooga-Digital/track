namespace Track.Services.Backups;

public static class DatabaseImages
{
    private static readonly string[] Names = ["postgres", "postgis", "timescaledb", "mariadb", "mysql", "percona", "mongo"];

    private static readonly string[] DataPaths =
    [
        "/var/lib/postgresql", "/var/lib/pgsql", "/var/lib/mysql", "/var/lib/mariadb", "/data/db",
        "/bitnami/postgresql", "/bitnami/mysql", "/bitnami/mariadb", "/bitnami/mongodb",
    ];

    public static bool IsDataPath(string path) =>
        DataPaths.Any(root => path.Equals(root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal));

    public static bool IsDatabase(string? image)
    {
        if (string.IsNullOrEmpty(image))
            return false;

        var repository = image.Split('@')[0];
        var colon = repository.LastIndexOf(':');
        if (colon > repository.LastIndexOf('/'))
            repository = repository[..colon];
        var name = repository[(repository.LastIndexOf('/') + 1)..].ToLowerInvariant();

        return Names.Any(name.Contains);
    }
}
