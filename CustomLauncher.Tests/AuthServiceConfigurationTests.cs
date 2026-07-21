using CustomLauncher.Core;

namespace CustomLauncher.Tests;

/// <summary>
/// The client id is injected rather than read from <see cref="LauncherConfig"/>. Reading the
/// constant made this suite drive a real interactive sign-in against Microsoft the moment an
/// operator filled it in, which is both slow and dependent on live services.
/// </summary>
public sealed class AuthServiceConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cl-auth-{Guid.NewGuid():N}");

    private AuthService CreateService(string clientId) =>
        new(new AppPaths(PlatformDetector.Current, _root,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["APPDATA"] = _root,
                ["LOCALAPPDATA"] = _root,
                ["XDG_CONFIG_HOME"] = _root,
                ["XDG_STATE_HOME"] = _root,
            },
            launcherId: "AuthTest"),
            clientId);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnUnconfiguredClientIdFailsInteractiveLoginClearly(string clientId)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(clientId).AuthenticateAsync());

        Assert.Contains("MicrosoftClientId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnconfiguredClientIdLeavesSilentRestoreQuiet()
    {
        // Startup calls this; if it throws, the launcher reports a startup failure instead of
        // simply showing the sign-in button.
        Assert.Null(await CreateService("").TryRestoreAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
