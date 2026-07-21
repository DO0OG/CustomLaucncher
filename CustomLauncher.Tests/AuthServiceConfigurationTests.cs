using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class AuthServiceConfigurationTests
{
    [Fact]
    public async Task MissingOperatorClientIdFailsInteractiveLoginClearly()
    {
        var service = new AuthService();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AuthenticateAsync());
        Assert.Contains("MicrosoftClientId", error.Message);
        Assert.Null(await service.TryRestoreAsync());
    }
}
