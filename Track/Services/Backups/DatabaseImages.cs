namespace Track.Services.Backups;

public static class DatabaseImages
{
    private static readonly string[] Names = ["postgres", "postgis", "timescaledb", "mariadb", "mysql", "percona", "mongo"];

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
