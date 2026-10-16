using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class AuthAccountTests
{
    private static readonly AuthAccount One = new("fake-home-id-1", "player-one");
    private static readonly AuthAccount Two = new("fake-home-id-2", "player-two");

    [Fact]
    public void PickAccountReturnsNullForAnEmptyList() =>
        Assert.Null(AuthService.PickAccount([], "fake-home-id-1"));

    [Fact]
    public void PickAccountReturnsTheOnlyAccount() =>
        Assert.Equal(One, AuthService.PickAccount([One], null));

    [Fact]
    public void PickAccountPrefersTheSavedId() =>
        Assert.Equal(Two, AuthService.PickAccount([One, Two], "fake-home-id-2"));

    [Theory]
    [InlineData(null)]
    [InlineData("fake-home-id-9")]
    public void PickAccountFallsBackToTheFirstAccount(string? savedId) =>
        Assert.Equal(One, AuthService.PickAccount([One, Two], savedId));

    [Fact]
    public void AccountTextNeverIncludesTheId()
    {
        Assert.Equal("player-one", One.ToString());
        Assert.DoesNotContain(One.Id, $"{One}", StringComparison.Ordinal);
    }
}
