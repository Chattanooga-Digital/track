using Track.Data.Models;
using Track.Services.Backups;

namespace Track.Website.Services.Backups;

/// <summary>Central: turns the stored target and the master key into restic connection details for one repository.</summary>
public sealed class ResticTargets(BackupSecretProtector protector, BackupMasterKey masterKey)
{
    private readonly BackupSecretProtector _protector = protector;
    private readonly BackupMasterKey _masterKey = masterKey;

    /// <param name="maintenance">Use the maintenance key, which may delete, when one is set.</param>
    public ResticTarget For(BackupTarget target, string repositoryKey, bool maintenance)
    {
        var key = _masterKey.Value ?? throw new InvalidOperationException("The backup master key isn't set.");
        var useMaintenance = maintenance
            && !string.IsNullOrEmpty(target.MaintenanceAccessKey)
            && !string.IsNullOrEmpty(target.MaintenanceSecretKeyProtected);

        return new ResticTarget(
            ResticRepositoryUrl.Build(target.Endpoint, target.Bucket, target.Prefix, repositoryKey),
            RepoPasswordDeriver.Derive(key, repositoryKey),
            useMaintenance ? target.MaintenanceAccessKey! : target.AgentAccessKey,
            _protector.Unprotect(useMaintenance ? target.MaintenanceSecretKeyProtected! : target.AgentSecretKeyProtected),
            target.Region,
            target.Addressing);
    }
}
