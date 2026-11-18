using System.Security.Cryptography;
using System.Text;

namespace Track.Services.Backups;

/// <summary>Same as <c>printf %s "$REPO" | openssl dgst -sha256 -hmac "$MASTER_KEY" -binary | base64</c>, so restic can open a repo without Track.</summary>
public static class RepoPasswordDeriver
{
    public static string Derive(string masterKey, string repositoryKey) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(masterKey), Encoding.UTF8.GetBytes(repositoryKey)));
}
