using Track.Configuration;
using Microsoft.Extensions.Options;

namespace Track.Services.Backups;

public sealed class BackupMasterKey(IOptions<TrackOptions> options)
{
    private readonly BackupOptions _options = options.Value.Backups;

    public string? Value
    {
        get
        {
            var key = !string.IsNullOrWhiteSpace(_options.MasterKey) ? _options.MasterKey
                : File.Exists(_options.MasterKeyFile) ? File.ReadAllText(_options.MasterKeyFile)
                : null;
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
    }

    public bool FromEnvironment => !string.IsNullOrWhiteSpace(_options.MasterKey);

    public string SecretName => Path.GetFileName(_options.MasterKeyFile);
}
