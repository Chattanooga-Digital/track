using Track.Website.Services;

namespace Track.Tests;

public class AgentSelfUpdaterTests
{
    [TestCase("0.9.1.22", "0.9.1.23", true)]
    [TestCase("0.9.1.23", "0.9.1.22", true)]
    [TestCase("0.9.1.23", "0.9.1.23", false)]
    [TestCase("0.9.1.23", null, false)]
    [TestCase("0.9.1.23", "0.9.1", false)]
    [TestCase("0.9.1.23", "unknown", false)]
    public void ShouldUpdate_FollowsTheCentralOnlyToCiBuilds(string current, string? central, bool expected)
    {
        Assert.That(AgentSelfUpdater.ShouldUpdate(current, central), Is.EqualTo(expected));
    }

    [TestCase("ghcr.io/chattanooga-digital/track:0.9.1.22", "ghcr.io/chattanooga-digital/track")]
    [TestCase("registry.example.com:5000/track:latest", "registry.example.com:5000/track")]
    [TestCase("registry.example.com:5000/track", "registry.example.com:5000/track")]
    [TestCase("ghcr.io/chattanooga-digital/track@sha256:abc", "ghcr.io/chattanooga-digital/track")]
    [TestCase("track", "track")]
    public void Repository_DropsTheTagButNotARegistryPort(string image, string expected)
    {
        Assert.That(AgentSelfUpdater.Repository(image), Is.EqualTo(expected));
    }

    [Test]
    public void ParseContainerId_ReadsTheIdFromDockersBindMounts()
    {
        const string id = "3f4e6d0f2b8a4c1e9d7b5a3c2e1f0a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f";
        var mountInfo =
            "512 498 0:45 / / rw,relatime master:1 - overlay overlay rw\n" +
            $"530 512 8:1 /var/lib/docker/containers/{id}/hostname /etc/hostname rw,relatime - ext4 /dev/sda1 rw\n";

        Assert.That(AgentSelfUpdater.ParseContainerId(mountInfo), Is.EqualTo(id));
        Assert.That(AgentSelfUpdater.ParseContainerId("512 498 0:45 / / rw - overlay overlay rw"), Is.Null);
    }

    [Test]
    public void OwnSettings_KeepsWhatTheContainerAddedOverItsImage()
    {
        var image = new List<string> { "PATH=/usr/bin", "DOTNET_VERSION=10.0.0" };
        var container = new List<string> { "PATH=/usr/bin", "DOTNET_VERSION=10.0.0", "Track__Mode=Agent", "Track__NodeName=ash-3" };

        Assert.That(AgentSelfUpdater.OwnSettings(container, image), Is.EqualTo(new[] { "Track__Mode=Agent", "Track__NodeName=ash-3" }));
    }
}
