using System.Reflection;

namespace Track.Website;

public static class AppVersion
{
    private static readonly string[] Parts = (Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+', 2);

    /// <summary>The full version without build metadata, e.g. 0.9.1.23; CI publishes the image under this tag.</summary>
    public static string Build { get; } = Parts[0].Length > 0 ? Parts[0] : "unknown";

    /// <summary>What the UI shows, e.g. 0.9.1+5e13aef.</summary>
    public static string Display { get; } = FormatDisplay(Build, Parts.Length == 2 ? Parts[1] : null);

    // CI's fourth version component is its run number, which the commit already pins down.
    private static string FormatDisplay(string build, string? commit)
    {
        var release = string.Join('.', build.Split('.').Take(3));
        return commit is null ? release : $"{release}+{commit[..Math.Min(7, commit.Length)]}";
    }
}
