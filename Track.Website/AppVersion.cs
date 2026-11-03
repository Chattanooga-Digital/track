using System.Reflection;

namespace Track.Website;

public static class AppVersion
{
    private static readonly string[] Parts = (Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+', 2);

    /// <summary>The full version without build metadata, e.g. 0.9.1.23; CI publishes the image under this tag.</summary>
    public static string Build { get; } = Parts[0].Length > 0 ? Parts[0] : "unknown";

    /// <summary>What the UI shows, e.g. 0.9.1.24+5e13aef.</summary>
    public static string Display { get; } =
        Parts is [_, var commit] ? $"{Build}+{commit[..Math.Min(7, commit.Length)]}" : Build;
}
