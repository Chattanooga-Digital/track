namespace Track.Services.Backups;

public static class ResticRepositoryUrl
{
    public static string Build(string endpoint, string bucket, string prefix, string repositoryKey)
    {
        var trimmed = prefix.Trim('/');
        return $"s3:{endpoint.Trim().TrimEnd('/')}/{bucket.Trim()}/{(trimmed.Length > 0 ? trimmed + "/" : "")}{repositoryKey}";
    }
}
