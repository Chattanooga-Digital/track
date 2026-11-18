using Track.Enums;
using Track.Services.Backups;
using Track.Services.Ingest;
using Track.Website.Services.Backups;

namespace Track.Tests;

public class ReplaceGuardTests
{
    private static CatalogVolume Volume(string node = "mgr-01", CatalogVolumeKind kind = CatalogVolumeKind.Single) =>
        new("app_db", "app_db", node, kind, "app", ["app_db"], ["postgres:17"], 1, null, null,
            [], [], [], [], [], true, false, [node], true);

    private static SwarmReport Swarm(List<string> constraints, bool global = false) => new("mgr-01", DateTime.UtcNow,
        [
            new SwarmNodeInfo("mgr-01", "manager", "active", "ready", []),
            new SwarmNodeInfo("mgr-02", "manager", "active", "ready", []),
        ],
        [new SwarmServiceInfo("app_db", "app", "postgres:17", global, global ? null : 1, ["app_db"], constraints, [], [])]);

    private static readonly HashSet<string> Ready = ["mgr-01", "mgr-02"];
    private static readonly Dictionary<string, NodeRole> Roles = new() { ["mgr-01"] = NodeRole.Manager, ["mgr-02"] = NodeRole.Manager };

    [Test]
    public void AServicePinnedToTheVolumesNodeCanBeReplaced()
    {
        Assert.That(BackupCatalogService.ReplaceBlockedReason(Volume(), Swarm(["node.hostname == mgr-01"]), Ready, Roles), Is.Null);
    }

    [Test]
    public void AServiceThatCanRunOnSeveralNodesIsBlocked()
    {
        var reason = BackupCatalogService.ReplaceBlockedReason(Volume(), Swarm(["node.role == manager"]), Ready, Roles);
        Assert.That(reason, Is.EqualTo("app_db can run on 2 nodes; pin it to mgr-01"));
    }

    [Test]
    public void AGlobalServiceIsBlocked()
    {
        var reason = BackupCatalogService.ReplaceBlockedReason(Volume(), Swarm([], global: true), Ready, Roles);
        Assert.That(reason, Is.EqualTo("app_db is a global service"));
    }

    [Test]
    public void ASwarmNodeWithoutASwarmViewIsBlocked()
    {
        var reason = BackupCatalogService.ReplaceBlockedReason(Volume(), null, Ready, Roles);
        Assert.That(reason, Is.EqualTo("No manager agent is reporting the swarm"));
    }

    [Test]
    public void AStandaloneHostsContainersCanBeReplaced()
    {
        var roles = new Dictionary<string, NodeRole> { ["host-1"] = NodeRole.Standalone };
        Assert.That(BackupCatalogService.ReplaceBlockedReason(Volume("host-1"), null, new HashSet<string> { "host-1" }, roles), Is.Null);
    }

    [Test]
    public void AnOfflineNodeOrALeftoverCopyIsBlocked()
    {
        Assert.That(BackupCatalogService.ReplaceBlockedReason(Volume(), Swarm(["node.hostname == mgr-01"]), new HashSet<string>(), Roles),
            Is.EqualTo("mgr-01 is offline or not approved"));
        Assert.That(BackupCatalogService.ReplaceBlockedReason(Volume(kind: CatalogVolumeKind.StaleCopy), null, Ready, Roles),
            Is.EqualTo("Only a volume with one live copy can be replaced"));
    }
}
