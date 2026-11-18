using Track.Services.Backups;

namespace Track.Tests;

public class BackupLabelsTests
{
    [TestCase("false", true)]
    [TestCase("False", true)]
    [TestCase("0", true)]
    [TestCase("true", false)]
    public void ExcludesService_OnlyWhenTheLabelSaysFalse(string value, bool expected)
    {
        var labels = new Dictionary<string, string> { [BackupLabels.Backup] = value };
        Assert.That(BackupLabels.ExcludesService(labels), Is.EqualTo(expected));
    }

    [TestCase("nextcloud_db")]
    [TestCase("db")]
    public void ExcludesVolume_AcceptsTheFullNameOrTheComposeKey(string labelName)
    {
        var labels = new Dictionary<string, string> { [$"backupbot.backup.volumes.{labelName}"] = "false" };
        Assert.That(BackupLabels.ExcludesVolume(labels, "nextcloud_db", "nextcloud"), Is.True);
        Assert.That(BackupLabels.ExcludesVolume(labels, "nextcloud_html", "nextcloud"), Is.False);
    }

    [Test]
    public void Paths_AreRelativeAndRefuseToLeaveTheVolume()
    {
        var labels = new Dictionary<string, string> { ["backupbot.backup.volumes.db.path"] = "/dump.sql, conf/, ../etc, ,dump.sql" };
        Assert.That(BackupLabels.Paths(labels, "app_db", "app"), Is.EqualTo(new[] { "dump.sql", "conf" }));
    }

    [Test]
    public void Select_KeepsOnlyBackupLabels()
    {
        var labels = new Dictionary<string, string>
        {
            [BackupLabels.PreHook] = "/pg_backup.sh backup",
            ["traefik.enable"] = "true",
        };
        Assert.That(BackupLabels.Select(labels).Keys, Is.EqualTo(new[] { BackupLabels.PreHook }));
    }

    [TestCase("postgres:17-alpine", true)]
    [TestCase("docker.io/library/mariadb:10.11", true)]
    [TestCase("postgis/postgis:16-3.4@sha256:abc", true)]
    [TestCase("registry.example.com:5000/mongo", true)]
    [TestCase("redis:7-alpine", false)]
    [TestCase("nextcloud:31", false)]
    public void DatabaseImages_RecogniseCommonDatabases(string image, bool expected)
    {
        Assert.That(DatabaseImages.IsDatabase(image), Is.EqualTo(expected));
    }
}
