using Track.Enums;
using Track.Services.Backups;
using Track.Website.Services.Backups;

namespace Track.Tests;

public class DatabaseDumpsTests
{
    [TestCase("postgres:18@sha256:7157393f", DatabaseEngine.Postgres)]
    [TestCase("postgis/postgis:16-3.4", DatabaseEngine.Postgres)]
    [TestCase("mariadb:10.11", DatabaseEngine.MariaDb)]
    [TestCase("registry.example.com:5000/library/mysql:8.0", DatabaseEngine.MySql)]
    [TestCase("bitnami/postgresql:16", null)]
    [TestCase("prometheuscommunity/postgres-exporter", null)]
    [TestCase("mongo:7", null)]
    public void EngineFor_KnowsTheImagesItCanDump(string image, DatabaseEngine? expected)
    {
        Assert.That(DatabaseDumps.EngineFor(image), Is.EqualTo(expected));
    }

    [Test]
    public void IsHarmlessImportError_OnlyForgivesTheSuperuserTheDumpRecreates()
    {
        Assert.That(DatabaseDumps.IsHarmlessImportError("psql:<stdin>:14: ERROR:  current user cannot be dropped"), Is.True);
        Assert.That(DatabaseDumps.IsHarmlessImportError("psql:<stdin>:15: ERROR:  role \"postgres\" already exists"), Is.True);
        Assert.That(DatabaseDumps.IsHarmlessImportError("psql:<stdin>:90: ERROR:  relation \"users\" already exists"), Is.False);
    }

    [Test]
    public void SnapshotIndex_ReadsDumpSnapshots()
    {
        const string json = """
            [{"time":"2026-11-19T01:49:27.001027829Z","paths":["/data/app_db/database.sql"],"hostname":"app_db",
              "tags":["track","stack=app","dump=postgres"],"id":"c6b8bfcb"}]
            """;

        var snapshot = SnapshotIndex.Parse(json)["c6b8bfcb"];

        Assert.That((snapshot.VolumeName, snapshot.DumpEngine, snapshot.Paths), Is.EqualTo(("app_db", (DatabaseEngine?)DatabaseEngine.Postgres, "")));
        Assert.That(snapshot.Time, Is.EqualTo(new DateTime(2026, 11, 19, 1, 49, 27, 1, DateTimeKind.Utc)).Within(TimeSpan.FromMilliseconds(1)));
    }
}
