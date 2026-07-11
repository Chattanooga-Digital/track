using Track.Services.Docker;

namespace Track.Tests;

public class ContainerGroupResolverTests
{
    [Test]
    public void ResolveIdentity_ReturnsFirstPresentInOrder()
    {
        var env = new Dictionary<string, string>
        {
            ["VIRTUAL_HOST"] = "vh.example",
            ["DISCOURSE_HOSTNAME"] = "venturing.chattanooga.digital",
        };

        // DISCOURSE_HOSTNAME is listed first, so it wins even though both are present.
        var result = ContainerGroupResolver.ResolveIdentity(
            ["DISCOURSE_HOSTNAME", "VIRTUAL_HOST"], env);

        Assert.That(result, Is.EqualTo("venturing.chattanooga.digital"));
    }

    [Test]
    public void ResolveIdentity_SkipsMissingAndEmptyValues()
    {
        var env = new Dictionary<string, string>
        {
            ["DISCOURSE_HOSTNAME"] = "",
            ["VIRTUAL_HOST"] = "vh.example",
        };

        var result = ContainerGroupResolver.ResolveIdentity(
            ["MISSING", "DISCOURSE_HOSTNAME", "VIRTUAL_HOST"], env);

        Assert.That(result, Is.EqualTo("vh.example"));
    }

    [Test]
    public void ResolveIdentity_ReturnsNullWhenListEmptyOrNoMatch()
    {
        var env = new Dictionary<string, string> { ["DISCOURSE_HOSTNAME"] = "x" };

        Assert.That(ContainerGroupResolver.ResolveIdentity([], env), Is.Null);
        Assert.That(ContainerGroupResolver.ResolveIdentity(["NOPE"], env), Is.Null);
    }

    [Test]
    public void ResolveIdentity_IsCaseSensitiveOnEnvKey()
    {
        var env = new Dictionary<string, string> { ["DISCOURSE_HOSTNAME"] = "x" };

        Assert.That(ContainerGroupResolver.ResolveIdentity(["discourse_hostname"], env), Is.Null);
    }

    [Test]
    public void ResolveIdentity_SkipsSensitiveKeysEvenIfConfigured()
    {
        var env = new Dictionary<string, string>
        {
            ["API_TOKEN"] = "should-not-leak",
            ["VIRTUAL_HOST"] = "vh.example",
        };

        // API_TOKEN contains "token", so it is skipped even though it is listed first.
        var result = ContainerGroupResolver.ResolveIdentity(["API_TOKEN", "VIRTUAL_HOST"], env);

        Assert.That(result, Is.EqualTo("vh.example"));
    }

    [Test]
    public void SensitiveEnvKeys_FlagsSecretLikeNames()
    {
        Assert.That(SensitiveEnvKeys.IsSensitive("DISCOURSE_SMTP_PASSWORD"), Is.True);
        Assert.That(SensitiveEnvKeys.IsSensitive("API_TOKEN"), Is.True);
        Assert.That(SensitiveEnvKeys.IsSensitive("AWS_SECRET_ACCESS_KEY"), Is.True);
        Assert.That(SensitiveEnvKeys.IsSensitive("DISCOURSE_HOSTNAME"), Is.False);
        Assert.That(SensitiveEnvKeys.IsSensitive("VIRTUAL_HOST"), Is.False);
    }

    [Test]
    public void ResolveIdentity_TruncatesToStackNameLength()
    {
        var value = new string('a', 250);
        var env = new Dictionary<string, string> { ["ID"] = value };

        var result = ContainerGroupResolver.ResolveIdentity(["ID"], env);

        Assert.That(result, Has.Length.EqualTo(200));
    }

    [Test]
    public void ParseEnv_SplitsOnFirstEquals()
    {
        var env = ContainerGroupResolver.ParseEnv(["DB_URL=postgres://u:p@host/db?x=1", "EMPTY=", "NOEQUALS"]);

        Assert.That(env["DB_URL"], Is.EqualTo("postgres://u:p@host/db?x=1"));
        Assert.That(env["EMPTY"], Is.EqualTo(string.Empty));
        Assert.That(env.ContainsKey("NOEQUALS"), Is.False);
    }
}
