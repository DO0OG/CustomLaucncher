using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests.Core;

public sealed class ModuleValidationTests
{
    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("mods/../../evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("/tmp/evil.txt")]
    [InlineData("mods//evil.jar")]
    public void ResolvePath_RejectsEscapeAndAmbiguousPaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => ModuleValidation.ResolvePath(Path.GetTempPath(), path));
    }

    [Fact]
    public void ValidateDistribution_RejectsHttpAndParentCycles()
    {
        var distro = new ServerDistribution
        {
            ServerId = "test",
            Modules =
            [
                Module("a", "mods/a.jar", "http://example.test/a", "b"),
                Module("b", "mods/b.jar", "https://example.test/b", "a"),
            ],
        };
        Assert.Throws<InvalidDataException>(() => ModuleValidation.ValidateDistribution(distro));
    }

    private static DistroModule Module(string id, string path, string url, string? parent = null) => new()
    {
        Id = id,
        Path = path,
        Url = url,
        Hash = new string('0', 64),
        ParentId = parent,
        IsServerManaged = true,
    };
}
