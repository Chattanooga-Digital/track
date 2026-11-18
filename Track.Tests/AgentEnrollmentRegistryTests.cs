using Track.Data;
using Track.Enums;
using Track.Website.Services.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Track.Tests;

public class AgentEnrollmentRegistryTests
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _services = new ServiceCollection()
            .AddDbContext<TrackContext>(options => options.UseSqlite(_connection))
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TrackContext>().Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AgentEnrollmentRegistry NewRegistry() => new(_services.GetRequiredService<IServiceScopeFactory>());

    [Test]
    public async Task ANewNodeWaitsForApprovalThenOnlyItsTokenIsTrusted()
    {
        var registry = NewRegistry();

        Assert.That(await registry.CheckAsync("node-1", "token-a", default), Is.EqualTo(AgentApproval.Pending));
        Assert.That(registry.ApprovedNodeFor("token-a"), Is.Null);

        await registry.ApproveAsync("node-1", "admin", default);

        Assert.That(await registry.CheckAsync("node-1", "token-a", default), Is.EqualTo(AgentApproval.Approved));
        Assert.That(registry.ApprovedNodeFor("token-a"), Is.EqualTo("node-1"));
        Assert.That(await registry.CheckAsync("node-1", "token-b", default), Is.EqualTo(AgentApproval.Pending));
        Assert.That(registry.States()["node-1"].TokenChanged, Is.True);
        Assert.That(registry.ApprovedNodeFor("token-a"), Is.EqualTo("node-1"), "the approved token keeps working");
    }

    [Test]
    public async Task ApprovingAChangedTokenMovesTrustToIt()
    {
        var registry = NewRegistry();
        await registry.CheckAsync("node-1", "token-a", default);
        await registry.ApproveAsync("node-1", "admin", default);
        await registry.CheckAsync("node-1", "token-b", default);

        await registry.ApproveAsync("node-1", "admin", default);

        Assert.That(registry.ApprovedNodeFor("token-a"), Is.Null);
        Assert.That(registry.ApprovedNodeFor("token-b"), Is.EqualTo("node-1"));
        Assert.That(registry.States()["node-1"].TokenChanged, Is.False);
    }

    [Test]
    public async Task ARevokedTokenStaysRevokedButARebuiltNodeAsksAgain()
    {
        var registry = NewRegistry();
        await registry.CheckAsync("node-1", "token-a", default);
        await registry.ApproveAsync("node-1", "admin", default);
        await registry.RevokeAsync("node-1", default);

        Assert.That(await registry.CheckAsync("node-1", "token-a", default), Is.EqualTo(AgentApproval.Revoked));
        Assert.That(await registry.CheckAsync("node-1", "token-c", default), Is.EqualTo(AgentApproval.Pending));
    }

    [Test]
    public async Task ApprovalSurvivesARestart()
    {
        var registry = NewRegistry();
        await registry.CheckAsync("node-1", "token-a", default);
        await registry.ApproveAsync("node-1", "admin", default);

        var reloaded = NewRegistry();
        await reloaded.LoadAsync(default);

        Assert.That(reloaded.ApprovedNodeFor("token-a"), Is.EqualTo("node-1"));
    }

    [Test]
    public async Task AnAgentWithoutATokenIsNeverApproved()
    {
        var registry = NewRegistry();
        Assert.That(await registry.CheckAsync("node-1", null, default), Is.EqualTo(AgentApproval.Pending));
        Assert.That(await registry.ApproveAsync("node-1", "admin", default), Is.False);
    }
}
