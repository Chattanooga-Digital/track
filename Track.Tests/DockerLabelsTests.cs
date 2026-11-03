using Track.Services.Docker;

namespace Track.Tests;

public class DockerLabelsTests
{
    [Test]
    public void GetStackName_PrefersSwarmStackNamespace()
    {
        var labels = new Dictionary<string, string>
        {
            [DockerLabels.StackNamespace] = "acme-web",
            [DockerLabels.ComposeProject] = "compose-project",
        };

        Assert.That(DockerLabels.GetStackName(labels), Is.EqualTo("acme-web"));
    }

    [Test]
    public void GetStackName_FallsBackToComposeProject()
    {
        var labels = new Dictionary<string, string>
        {
            [DockerLabels.ComposeProject] = "compose-project",
        };

        Assert.That(DockerLabels.GetStackName(labels), Is.EqualTo("compose-project"));
    }

    [Test]
    public void GetStackName_FallsBackToTrackStackLabel()
    {
        var labels = new Dictionary<string, string>
        {
            [DockerLabels.TrackStack] = "track",
        };

        Assert.That(DockerLabels.GetStackName(labels), Is.EqualTo("track"));
    }

    [Test]
    public void GetStackName_ReturnsNullWithoutStackLabels()
    {
        Assert.That(DockerLabels.GetStackName(new Dictionary<string, string>()), Is.Null);
        Assert.That(DockerLabels.GetStackName(null), Is.Null);
    }

    [Test]
    public void GetServiceName_PrefersSwarmServiceName()
    {
        var labels = new Dictionary<string, string>
        {
            [DockerLabels.SwarmServiceName] = "acme-web_api",
            [DockerLabels.ComposeService] = "api",
        };

        Assert.That(DockerLabels.GetServiceName(labels), Is.EqualTo("acme-web_api"));
    }
}
