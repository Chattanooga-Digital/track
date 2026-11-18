using Track.Enums;
using Track.Services.Backups;
using Track.Services.Ingest;
using Track.Website.Services.Backups;

namespace Track.Tests;

public class BackupPrimitivesTests
{
    [Test]
    public void RepoPassword_MatchesTheOpensslRecoveryCommand()
    {
        // printf %s "customer-12" | openssl dgst -sha256 -hmac "correct horse battery staple" -binary | base64
        Assert.That(RepoPasswordDeriver.Derive("correct horse battery staple", "customer-12"),
            Is.EqualTo("J4nbu5DxPQ9YFsO9Vr1XzLjtkNRGG58agM1vnYxEOqk="));
    }

    [TestCase("https://s3.example.com/", "track/", "s3:https://s3.example.com/backups/track/shared")]
    [TestCase("https://s3.example.com", "/track", "s3:https://s3.example.com/backups/track/shared")]
    [TestCase("http://minio:9000", "", "s3:http://minio:9000/backups/shared")]
    public void RepositoryUrl_JoinsEndpointBucketPrefixAndKey(string endpoint, string prefix, string expected)
    {
        Assert.That(ResticRepositoryUrl.Build(endpoint, "backups", prefix, "shared"), Is.EqualTo(expected));
    }

    [Test]
    public void ResticTarget_PassesAddressingAndRegionAsOptions()
    {
        var target = new ResticTarget("s3:x", "p", "a", "s", "eu-west-1", S3Addressing.Path);
        Assert.That(target.Options, Is.EqualTo(new[] { "-o", "s3.bucket-lookup=path", "-o", "s3.region=eu-west-1" }));
    }

    [Test]
    public void Offset_IsStableAndInsideTheWindow()
    {
        var offsets = Enumerable.Range(0, 200).Select(i => BackupSchedule.OffsetMinutes($"volume-{i}", 180)).ToList();
        Assert.That(offsets, Has.All.InRange(0, 179));
        Assert.That(offsets.Distinct().Count(), Is.GreaterThan(50));
        var first = BackupSchedule.OffsetMinutes("app_db", 180);
        var again = BackupSchedule.OffsetMinutes("app_db", 180);
        Assert.That(again, Is.EqualTo(first));
        Assert.That(BackupSchedule.OffsetMinutes("app_db", 0), Is.Zero);
    }

    [Test]
    public void DailySlot_IsTodayOnceItHasPassedOtherwiseYesterday()
    {
        var before = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);
        var after = new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

        Assert.That(BackupSchedule.MostRecentDailySlot(before, 120, 30), Is.EqualTo(new DateTime(2026, 11, 17, 2, 30, 0)));
        Assert.That(BackupSchedule.MostRecentDailySlot(after, 120, 30), Is.EqualTo(new DateTime(2026, 11, 18, 2, 30, 0)));
        // A late window that runs past midnight still lands on a real past slot.
        Assert.That(BackupSchedule.MostRecentDailySlot(before, 23 * 60, 179), Is.EqualTo(new DateTime(2026, 11, 17, 1, 59, 0)));
    }

    [Test]
    public void WeeklySlot_FindsTheLastPrunedDay()
    {
        var wednesday = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        var sundayEarly = new DateTime(2026, 11, 22, 1, 0, 0, DateTimeKind.Utc);

        Assert.That(BackupSchedule.MostRecentWeeklySlot(wednesday, DayOfWeek.Sunday, 360), Is.EqualTo(new DateTime(2026, 11, 15, 6, 0, 0)));
        Assert.That(BackupSchedule.MostRecentWeeklySlot(sundayEarly, DayOfWeek.Sunday, 360), Is.EqualTo(new DateTime(2026, 11, 15, 6, 0, 0)));
    }

    private static readonly List<SwarmNodeInfo> Nodes =
    [
        new("mgr-01", "manager", "active", "ready", new Dictionary<string, string> { ["db"] = "true" }),
        new("mgr-02", "manager", "active", "ready", []),
        new("wkr-01", "worker", "active", "ready", []),
        new("wkr-02", "worker", "drain", "ready", []),
    ];

    [Test]
    public void Placement_ResolvesRolesHostnamesAndLabels()
    {
        Assert.That(PlacementEvaluator.EligibleNodes(["node.role == manager"], Nodes), Is.EqualTo(new[] { "mgr-01", "mgr-02" }));
        Assert.That(PlacementEvaluator.EligibleNodes(["node.hostname==mgr-02"], Nodes), Is.EqualTo(new[] { "mgr-02" }));
        Assert.That(PlacementEvaluator.EligibleNodes(["node.labels.db == true"], Nodes), Is.EqualTo(new[] { "mgr-01" }));
        Assert.That(PlacementEvaluator.EligibleNodes(["node.role != manager"], Nodes), Is.EqualTo(new[] { "wkr-01" }));
        Assert.That(PlacementEvaluator.EligibleNodes([], Nodes), Has.Count.EqualTo(3));
    }

    [Test]
    public void Placement_IsUnknownForAttributesItCantEvaluate()
    {
        Assert.That(PlacementEvaluator.EligibleNodes(["node.platform.os == linux"], Nodes), Is.Null);
        Assert.That(PlacementEvaluator.EligibleNodes(["engine.labels.x == y"], Nodes), Is.Null);
    }

    [Test]
    public void ResticJson_ReadsBackupStatusAndSummary()
    {
        var status = ResticJson.ParseLine("""{"message_type":"status","percent_done":0.5,"total_files":10,"total_bytes":2000,"bytes_done":1000}""");
        var summary = ResticJson.ParseLine("""{"message_type":"summary","files_new":3,"files_changed":1,"data_added":900,"data_added_packed":400,"total_bytes_processed":2000,"snapshot_id":"abc123"}""");

        Assert.That(status, Is.EqualTo(new ResticStatus(0.5, 1000, 2000)));
        Assert.That(summary, Is.EqualTo(new ResticSummary("abc123", 400, 3, 1, 2000)));
        Assert.That(ResticJson.ParseLine("Fatal: unable to open repository"), Is.Null);
    }

    [Test]
    public void SnapshotIndex_ParsesTracksSnapshotsAndSkipsOthers()
    {
        const string json = """
            [
              {"time":"2026-11-18T02:14:00.123Z","hostname":"app_db","paths":["/data/app_db"],"tags":["track","stack=app","node=mgr-01"],"id":"aaa"},
              {"time":"2026-11-17T02:14:00Z","hostname":"app_db","paths":["/data/app_db/dump.sql"],"tags":["track-pre-restore"],"id":"bbb"},
              {"time":"2026-11-17T02:14:00Z","hostname":"laptop","paths":["/home/me"],"tags":["track"],"id":"ccc"}
            ]
            """;

        var parsed = SnapshotIndex.Parse(json);

        Assert.That(parsed.Keys, Is.EquivalentTo(new[] { "aaa", "bbb" }));
        Assert.That(parsed["aaa"].NodeName, Is.EqualTo("mgr-01"));
        Assert.That(parsed["aaa"].StackName, Is.EqualTo("app"));
        Assert.That(parsed["aaa"].Paths, Is.Empty);
        Assert.That(parsed["bbb"].Paths, Is.EqualTo("dump.sql"));
        Assert.That(parsed["bbb"].PreRestore, Is.True);
        Assert.That(parsed["aaa"].Time.Kind, Is.EqualTo(DateTimeKind.Utc));
    }
}
