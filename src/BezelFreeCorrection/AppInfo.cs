using System.Reflection;

namespace BezelFreeCorrection;

// Single source of truth for the user-visible version and product
// name. Reads the assembly's informational version so changing the
// csproj's <Version> automatically propagates to the splash, HUD and
// update checker without shadowing copies to update by hand.
public static class AppInfo
{
    public const string ProductName = "Wallpaper Bezel Free Correction";

    public const string GitHubOwner = "BrunoSilva1978PT";
    public const string GitHubRepo  = "bezel-free-negative-correction";

    public static string Version => _version.Value;

    private static readonly System.Lazy<string> _version = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // Informational version may include a commit suffix like "1.0.0+abc".
            var plus = info.IndexOf('+');
            return plus < 0 ? info : info[..plus];
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    });
}
