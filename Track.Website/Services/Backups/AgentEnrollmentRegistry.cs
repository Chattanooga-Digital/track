using System.Security.Cryptography;
using System.Text;
using Track.Data;
using Track.Data.Models;
using Track.Enums;
using Microsoft.EntityFrameworkCore;

namespace Track.Website.Services.Backups;

public sealed record AgentEnrollmentState(AgentApproval Approval, bool TokenChanged);

/// <summary>Central: ties each node name to one token. Only an approved token's reports are trusted, since they decide what runs where.</summary>
public sealed class AgentEnrollmentRegistry(IServiceScopeFactory scopeFactory)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, AgentEnrollment> _byNode = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        _byNode = await context.AgentEnrollments.AsNoTracking().ToDictionaryAsync(e => e.NodeName, cancellationToken);
    }

    public async Task<AgentApproval> CheckAsync(string nodeName, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return AgentApproval.Pending;

        var hash = Hash(token);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_byNode.TryGetValue(nodeName, out var enrollment))
            {
                await SaveAsync(nodeName, e =>
                {
                    e.TokenHash = hash;
                    e.Status = AgentApproval.Pending;
                    e.FirstSeenAt = DateTime.UtcNow;
                }, cancellationToken);
                return AgentApproval.Pending;
            }

            switch (enrollment.Status)
            {
                case AgentApproval.Approved when enrollment.TokenHash == hash:
                    return AgentApproval.Approved;
                case AgentApproval.Approved:
                    if (enrollment.PendingTokenHash != hash)
                        await SaveAsync(nodeName, e => e.PendingTokenHash = hash, cancellationToken);
                    return AgentApproval.Pending;
                case AgentApproval.Revoked when enrollment.TokenHash == hash:
                    return AgentApproval.Revoked;
                default:
                    if (enrollment.TokenHash != hash || enrollment.Status != AgentApproval.Pending)
                    {
                        await SaveAsync(nodeName, e =>
                        {
                            e.TokenHash = hash;
                            e.Status = AgentApproval.Pending;
                            e.PendingTokenHash = null;
                        }, cancellationToken);
                    }
                    return AgentApproval.Pending;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The approved node a token belongs to, or null.</summary>
    public string? ApprovedNodeFor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var hash = Hash(token);
        return _byNode.Values.FirstOrDefault(e => e.Status == AgentApproval.Approved && e.TokenHash == hash)?.NodeName;
    }

    public bool IsApproved(string nodeName) =>
        _byNode.TryGetValue(nodeName, out var enrollment) && enrollment.Status == AgentApproval.Approved;

    public IReadOnlyDictionary<string, AgentEnrollmentState> States() =>
        _byNode.Values.ToDictionary(e => e.NodeName, e => new AgentEnrollmentState(e.Status, e.PendingTokenHash is not null));

    /// <summary>False when there is nothing to approve: the node has never sent a token.</summary>
    public async Task<bool> ApproveAsync(string nodeName, string? approvedBy, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_byNode.TryGetValue(nodeName, out var enrollment))
                return false;

            await SaveAsync(nodeName, e =>
            {
                if (e.PendingTokenHash is not null)
                    e.TokenHash = e.PendingTokenHash;
                e.PendingTokenHash = null;
                e.Status = AgentApproval.Approved;
                e.ApprovedAt = DateTime.UtcNow;
                e.ApprovedBy = approvedBy;
            }, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(string nodeName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_byNode.ContainsKey(nodeName))
                return false;

            await SaveAsync(nodeName, e =>
            {
                e.Status = AgentApproval.Revoked;
                e.PendingTokenHash = null;
            }, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string nodeName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
            await context.AgentEnrollments.Where(e => e.NodeName == nodeName).ExecuteDeleteAsync(cancellationToken);
            var copy = new Dictionary<string, AgentEnrollment>(_byNode);
            copy.Remove(nodeName);
            _byNode = copy;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(string nodeName, Action<AgentEnrollment> change, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TrackContext>();
        var enrollment = await context.AgentEnrollments.SingleOrDefaultAsync(e => e.NodeName == nodeName, cancellationToken);
        if (enrollment is null)
        {
            enrollment = new AgentEnrollment { NodeName = nodeName, TokenHash = "" };
            context.AgentEnrollments.Add(enrollment);
        }

        change(enrollment);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(enrollment).State = EntityState.Detached;

        // Readers enumerate the dictionary without the gate, so swap in a new one rather than mutating it.
        _byNode = new Dictionary<string, AgentEnrollment>(_byNode) { [nodeName] = enrollment };
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
