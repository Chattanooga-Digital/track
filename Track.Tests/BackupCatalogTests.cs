using Track.Enums;
using Track.Services;
using Track.Services.Backups;
using Track.Services.Ingest;

namespace Track.Tests;

public class BackupCatalogTests
{
    private static VolumeMount Mount(string volume, string service, string image = "app:1", Dictionary<string, string>? labels = null) =>
        new(volume, $"{service}-id", $"{service}.1", "app", service, image, labels ?? []);

    private static CatalogInputs Inputs(
        Dictionary<string, List<NodeVolume>> volumes,
        Dictionary<string, List<VolumeMount>> mounts,
        SwarmReport? swarm = null,
        Dictionary<string, VolumeOverride>? overrides = null) =>
        new(volumes, mounts, swarm, overrides ?? [], new HashSet<string> { "track-restic-cache" });

    private static List<CatalogVolume> Build(
        Dictionary<string, List<NodeVolume>> volumes,
        Dictionary<string, List<VolumeMount>> mounts,
        SwarmReport? swarm = null,
        Dictionary<string, VolumeOverride>? overrides = null) =>
        BackupCatalog.Build(Inputs(volumes, mounts, swarm, overrides));

    [Test]
    public void Build_BacksUpTheCopyInUseAndFlagsLeftovers()
    {
        var catalog = Build(
            new()
            {
                ["node-1"] = [new NodeVolume("app_db", "app", 100)],
                ["node-2"] = [new NodeVolume("app_db", "app", 90)],
            },
            new() { ["node-1"] = [Mount("app_db", "app_db", "postgres:17")] });

        Assert.That(catalog.Select(v => (v.Key, v.NodeName, v.Kind)), Is.EqualTo(new[]
        {
            ("app_db", (string?)"node-1", CatalogVolumeKind.Single),
            ("app_db@node-2", (string?)"node-2", CatalogVolumeKind.StaleCopy),
        }));
        Assert.That(catalog[0].Protected, Is.True);
        Assert.That(catalog[0].Method, Is.EqualTo(BackupMethod.Dump));
        Assert.That(catalog[1].Protected, Is.False);
    }

    [Test]
    public void Build_DumpsOnlyADatabasesDataDirectory()
    {
        var catalog = Build(
            new() { ["node-1"] = [new NodeVolume("app_db", "app", 1), new NodeVolume("app_dbinit", "app", 1)] },
            new()
            {
                ["node-1"] =
                [
                    Mount("app_db", "app_db", "mariadb:11") with { Destination = "/var/lib/mysql" },
                    Mount("app_dbinit", "app_db", "mariadb:11") with { Destination = "/docker-entrypoint-initdb.d" },
                ],
            });

        var data = catalog.Single(v => v.VolumeName == "app_db");
        var init = catalog.Single(v => v.VolumeName == "app_dbinit");
        Assert.That((data.Method, data.Engine, data.DatabaseService), Is.EqualTo((BackupMethod.Dump, (DatabaseEngine?)DatabaseEngine.MariaDb, (string?)"app_db")));
        Assert.That((init.Method, init.DatabaseWithoutDump), Is.EqualTo((BackupMethod.Files, false)));
    }

    [Test]
    public void Build_FlagsADatabaseItCantDump()
    {
        var catalog = Build(
            new() { ["node-1"] = [new NodeVolume("app_mongo", "app", 1), new NodeVolume("app_pg", "app", 1)] },
            new()
            {
                ["node-1"] =
                [
                    Mount("app_mongo", "app_mongo", "mongo:7") with { Destination = "/data/db" },
                    Mount("app_pg", "app_pg", "postgres:17") with { Destination = "/var/lib/postgresql/data" },
                ],
            },
            overrides: new() { ["app_pg"] = new VolumeOverride(null, BackupMethod.Files) });

        Assert.That(catalog.Single(v => v.VolumeName == "app_mongo").DatabaseWithoutDump, Is.True);
        Assert.That(catalog.Single(v => v.VolumeName == "app_pg").DatabaseWithoutDump, Is.True);
    }

    [Test]
    public void Build_IgnoresEmptyVolumesUntilTheyHoldData()
    {
        var catalog = Build(
            new()
            {
                ["node-1"] =
                [
                    new NodeVolume("app_empty", "app", 0),
                    new NodeVolume("app_kept", "app", 0),
                ],
            },
            new() { ["node-1"] = [Mount("app_empty", "app"), Mount("app_kept", "app"), Mount("app_unmeasured", "app")] },
            overrides: new() { ["app_kept"] = new VolumeOverride(false, null) });

        Assert.That(catalog.Single(v => v.VolumeName == "app_empty").ExcludedBy, Is.EqualTo(ExclusionSource.Empty));
        Assert.That(catalog.Single(v => v.VolumeName == "app_kept").Protected, Is.True);
        Assert.That(catalog.Single(v => v.VolumeName == "app_unmeasured").Protected, Is.True);
    }

    [Test]
    public void Build_SkipsAnonymousVolumesAndWarnsWhenADatabaseUsesOne()
    {
        var anonymous = new string('b', 64);
        var inputs = Inputs(
            new() { ["node-1"] = [new NodeVolume(anonymous, null, 10), new NodeVolume("app_cache", "app", 10)] },
            new()
            {
                ["node-1"] =
                [
                    Mount(anonymous, "app_db", "postgres:18") with { Destination = "/var/lib/postgresql" },
                    Mount("app_cache", "app_redis", "redis:7") with { Destination = "/data" },
                ],
            });

        Assert.That(BackupCatalog.Build(inputs).Select(v => v.Key), Is.EqualTo(new[] { "app_cache" }));
        Assert.That(BackupCatalog.AnonymousDatabases(inputs), Is.EqualTo(new[] { new AnonymousDatabase("node-1", "app_db", "app") }));
    }

    [Test]
    public void Build_TreatsAGlobalServicesVolumeAsOnePerNode()
    {
        var swarm = new SwarmReport("node-1", DateTime.UtcNow, [], [
            new SwarmServiceInfo("agent_agent", "agent", "agent:1", true, null, ["agent_data"], [], [], []),
        ]);
        var catalog = Build(
            new()
            {
                ["node-1"] = [new NodeVolume("agent_data", "agent", 1)],
                ["node-2"] = [new NodeVolume("agent_data", "agent", 1)],
            },
            new() { ["node-1"] = [Mount("agent_data", "agent_agent")] },
            swarm);

        Assert.That(catalog.Select(v => v.Key), Is.EqualTo(new[] { "agent_data@node-1", "agent_data@node-2" }));
        Assert.That(catalog.All(v => v.Kind == CatalogVolumeKind.PerNode && v.Protected), Is.True);
    }

    [Test]
    public void Build_CantPickBetweenUnusedCopies()
    {
        var catalog = Build(
            new()
            {
                ["node-1"] = [new NodeVolume("old_data", null, 1)],
                ["node-2"] = [new NodeVolume("old_data", null, 1)],
            },
            []);

        Assert.That(catalog.Single().Kind, Is.EqualTo(CatalogVolumeKind.Ambiguous));
        Assert.That(catalog.Single().Protected, Is.False);
    }

    [Test]
    public void Build_SkipsTracksOwnAndLeftoverVolumes()
    {
        var anonymous = new string('a', 64);
        var catalog = Build(
            new()
            {
                ["node-1"] =
                [
                    new NodeVolume("track-restic-cache", null, 1),
                    new NodeVolume("app_db-restore-20261118-120000", null, 1),
                    new NodeVolume(anonymous, null, 1),
                    new NodeVolume("app_db", "app", 1),
                ],
            },
            []);

        Assert.That(catalog.Select(v => v.Key), Is.EqualTo(new[] { "app_db" }));
    }

    [Test]
    public void Build_ServiceLabelsWinAndTrackOverridesWinOverLabels()
    {
        var swarm = new SwarmReport("node-1", DateTime.UtcNow, [], [
            new SwarmServiceInfo("app_db", "app", "postgres:17", false, 1, ["app_db"], [],
                new Dictionary<string, string>
                {
                    [BackupLabels.PreHook] = "/pg_backup.sh backup",
                    [BackupLabels.RestorePostHook] = "/pg_backup.sh restore",
                    ["backupbot.backup.volumes.cache"] = "false",
                },
                [new SwarmTaskInfo("node-1", "c1")]),
        ]);
        var volumes = new Dictionary<string, List<NodeVolume>>
        {
            ["node-1"] = [new NodeVolume("app_db", "app", 1), new NodeVolume("app_cache", "app", 1)],
        };
        var mounts = new Dictionary<string, List<VolumeMount>>
        {
            ["node-1"] = [Mount("app_db", "app_db", "postgres:17"), Mount("app_cache", "app_db", "postgres:17")],
        };

        var catalog = Build(volumes, mounts, swarm with
        {
            Services = [swarm.Services[0] with { Volumes = ["app_db", "app_cache"] }],
        });
        var db = catalog.Single(v => v.Key == "app_db");
        var cache = catalog.Single(v => v.Key == "app_cache");

        Assert.That(db.PreHooks, Is.EqualTo(new[] { new AgentHook("app_db", "/pg_backup.sh backup") }));
        Assert.That(db.RestorePostHooks.Single().Command, Is.EqualTo("/pg_backup.sh restore"));
        Assert.That((db.Method, db.DatabaseWithoutDump), Is.EqualTo((BackupMethod.Files, false)));
        Assert.That(cache.ExcludedBy, Is.EqualTo(ExclusionSource.Label));

        var overridden = Build(volumes, mounts, swarm with
        {
            Services = [swarm.Services[0] with { Volumes = ["app_db", "app_cache"] }],
        }, new() { ["app_cache"] = new VolumeOverride(false, null), ["app_db"] = new VolumeOverride(true, null) });
        Assert.That(overridden.Single(v => v.Key == "app_cache").Excluded, Is.False);
        Assert.That(overridden.Single(v => v.Key == "app_db").ExcludedBy, Is.EqualTo(ExclusionSource.Track));
    }
}
