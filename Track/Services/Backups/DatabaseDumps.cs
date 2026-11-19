using Track.Enums;

namespace Track.Services.Backups;

/// <summary>
/// The fixed commands Track runs inside a database container. They name credentials by variable,
/// so the container's own shell fills them in and the values never leave the node.
/// </summary>
public static class DatabaseDumps
{
    public const string FileName = "database.sql";

    private const string PostgresUser = "u=\"${POSTGRES_USER:-postgres}\"";

    private const string MySqlLogin = """
        p="${MARIADB_ROOT_PASSWORD:-$MYSQL_ROOT_PASSWORD}"
        f="${MARIADB_ROOT_PASSWORD_FILE:-$MYSQL_ROOT_PASSWORD_FILE}"
        if [ -z "$p" ] && [ -n "$f" ] && [ -r "$f" ]; then p="$(cat "$f")"; fi
        if [ -n "$p" ]; then export MYSQL_PWD="$p"; fi
        c="$(command -v mariadb || command -v mysql)"
        """;

    private const string SystemSchemas = "'mysql','information_schema','performance_schema','sys'";

    public static DatabaseEngine? EngineFor(string? image)
    {
        if (string.IsNullOrEmpty(image))
            return null;

        var repository = image.Split('@')[0];
        var colon = repository.LastIndexOf(':');
        if (colon > repository.LastIndexOf('/'))
            repository = repository[..colon];

        // Bitnami images use their own variable names and run as a non-root user, so the commands below don't fit them.
        if (repository.Contains("bitnami/", StringComparison.OrdinalIgnoreCase))
            return null;

        return repository[(repository.LastIndexOf('/') + 1)..].ToLowerInvariant() switch
        {
            "postgres" or "postgis" or "timescaledb" => DatabaseEngine.Postgres,
            "mariadb" => DatabaseEngine.MariaDb,
            "mysql" or "percona" or "percona-server" => DatabaseEngine.MySql,
            _ => null,
        };
    }

    public static string Tag(DatabaseEngine engine) => engine.ToString().ToLowerInvariant();

    /// <summary>Writes the whole server's dump to stdout.</summary>
    public static string DumpCommand(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Postgres => $"""
            {PostgresUser}
            exec pg_dumpall -U "$u" --clean --if-exists
            """,
        _ => $"""
            {MySqlLogin}
            d="$(command -v mariadb-dump || command -v mysqldump)"
            dbs="$("$c" -uroot -N -B -e "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ({SystemSchemas})")" || exit 1
            [ -n "$dbs" ] || exit 0
            exec "$d" -uroot --single-transaction --routines --events --triggers --add-drop-database {(engine == DatabaseEngine.MySql ? "--set-gtid-purged=OFF " : "")}--databases $dbs
            """,
    };

    /// <summary>Reads a dump from stdin into the running server, replacing the databases it holds.</summary>
    public static string ImportCommand(DatabaseEngine engine) => engine switch
    {
        // The dump drops each database before recreating it, which fails while anything is still connected.
        DatabaseEngine.Postgres => $"""
            {PostgresUser}
            psql -X -q -U "$u" -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND datname IS NOT NULL" >/dev/null || exit 1
            exec psql -X -q -o /dev/null -U "$u" -d postgres
            """,
        _ => $"""
            {MySqlLogin}
            exec "$c" -uroot
            """,
    };

    /// <summary>Succeeds once the server takes connections over TCP, which the entrypoint's setup server never does.</summary>
    public static string ReadyCommand(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Postgres => "pg_isready -q -h 127.0.0.1",
        _ => $"""
            {MySqlLogin}
            "$c" -uroot -h 127.0.0.1 --protocol=tcp -N -B -e "SELECT 1" >/dev/null 2>&1
            """,
    };

    /// <summary>Prints how many databases hold tables, then how many tables there are in all.</summary>
    public static string CountCommand(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Postgres => $"""
            {PostgresUser}
            n=0; t=0
            for db in $(psql -X -At -U "$u" -d postgres -c "SELECT datname FROM pg_database WHERE NOT datistemplate"); do
              c="$(psql -X -At -U "$u" -d "$db" -c "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')")" || exit 1
              if [ "$c" -gt 0 ]; then n=$((n+1)); t=$((t+c)); fi
            done
            echo "$n $t"
            """,
        _ => $"""
            {MySqlLogin}
            "$c" -uroot -N -B -e "SELECT COUNT(DISTINCT table_schema), COUNT(*) FROM information_schema.tables WHERE table_schema NOT IN ({SystemSchemas})"
            """,
    };

    public static Dictionary<string, string> ScratchEnvironment(DatabaseEngine engine, string password) => engine switch
    {
        DatabaseEngine.Postgres => new() { ["POSTGRES_PASSWORD"] = password },
        DatabaseEngine.MariaDb => new() { ["MARIADB_ROOT_PASSWORD"] = password },
        _ => new() { ["MYSQL_ROOT_PASSWORD"] = password },
    };

    /// <summary>
    /// An error psql reports while importing that doesn't mean data is missing: a dump always drops and recreates
    /// the superuser it was taken as, which the server refuses for the user it's connected as.
    /// </summary>
    public static bool IsHarmlessImportError(string line) =>
        line.Contains("current user cannot be dropped", StringComparison.Ordinal)
        || (line.Contains("ERROR:  role \"", StringComparison.Ordinal) && line.Contains("\" already exists", StringComparison.Ordinal));

    public static bool IsImportError(string line) =>
        line.Contains("ERROR:", StringComparison.Ordinal) || line.StartsWith("ERROR ", StringComparison.Ordinal);
}
