using CmlLib.Core.Auth.Microsoft;
using CustomLauncher.Core;

namespace CustomLauncher.Tests;

/// <summary>
/// Minecraft's rejection has to reach the user as an explanation, not as "check the log". The first
/// attempt at this matched only <see cref="JEAuthException.StatusCode"/> and never fired, because
/// CmlLib leaves that at 0 whenever the error body fails to parse.
/// </summary>
public sealed class AuthErrorMappingTests
{
    [Fact]
    public void ForbiddenIsRecognisedWhenTheLibraryOnlyPutsTheCodeInTheMessage()
    {
        var exception = new JEAuthException("403: Forbidden");

        Assert.Equal(0, exception.StatusCode);
        Assert.True(AuthErrors.IsMinecraftForbidden(exception));
    }

    [Fact]
    public void ForbiddenIsRecognisedWhenTheLibraryParsesTheBody()
    {
        var exception = new JEAuthException("FORBIDDEN", "ForbiddenOperationException", "…", 403);

        Assert.True(AuthErrors.IsMinecraftForbidden(exception));
    }

    [Theory]
    [InlineData("The response was null.")]
    [InlineData("404: Not Found")]
    [InlineData("UserHash was empty. Xbox authentication is required.")]
    public void OtherFailuresAreNotReportedAsForbidden(string message) =>
        Assert.False(AuthErrors.IsMinecraftForbidden(new JEAuthException(message)));
}
