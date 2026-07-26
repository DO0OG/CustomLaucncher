using CmlLib.Core.Auth.Microsoft;

namespace CustomLauncher.Core;

/// <summary>Interprets authentication failures well enough to tell the user what to do about them.</summary>
public static class AuthErrors
{
    /// <summary>
    /// True when Minecraft itself refused the Xbox token. CmlLib only fills
    /// <see cref="JEAuthException.StatusCode"/> when the error body parses as its expected JSON
    /// shape; Minecraft's 403 body does not, so the library falls back to a "403: Forbidden" message
    /// and leaves StatusCode at 0. Checking only the property misses the most common failure.
    /// </summary>
    public static bool IsMinecraftForbidden(JEAuthException exception) =>
        exception.StatusCode == 403 ||
        exception.Message.StartsWith("403", StringComparison.Ordinal);
}
