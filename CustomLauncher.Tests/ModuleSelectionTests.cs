using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

public sealed class ModuleSelectionTests
{
    private static DistroModule Module(string id, ModuleType type, string? parent = null) => new()
    {
        Id = id,
        Path = $"mods/{id}.jar",
        Url = $"https://example.com/{id}.jar",
        Hash = new string('a', 64),
        Type = type,
        ParentId = parent,
        IsServerManaged = true,
    };

    private static ServerDistribution Distribution(params DistroModule[] modules) =>
        new() { ServerId = "test", Modules = [.. modules] };

    [Fact]
    public void DisablingAnOptionalModuleAlsoDisablesItsDescendants()
    {
        var distribution = Distribution(
            Module("core", ModuleType.RequiredMod),
            Module("optional", ModuleType.OptionalMod),
            Module("child", ModuleType.OptionalMod, "optional"),
            Module("grandchild", ModuleType.OptionalMod, "child"));

        var disabled = ModuleSelection.ResolveDisabled(distribution, ["optional"]);

        Assert.Equal(["child", "grandchild", "optional"], disabled.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void RequiredModulesCannotBeDisabled()
    {
        var distribution = Distribution(Module("core", ModuleType.RequiredMod));

        var disabled = ModuleSelection.ResolveDisabled(distribution, ["core"]);

        Assert.Empty(disabled);
    }

    [Fact]
    public void UnknownIdsAreIgnored()
    {
        var distribution = Distribution(Module("optional", ModuleType.OptionalMod));

        var disabled = ModuleSelection.ResolveDisabled(distribution, ["deleted-last-season"]);

        Assert.Empty(disabled);
    }

    [Fact]
    public void FilterRemovesDisabledModulesAndKeepsTheRest()
    {
        var distribution = Distribution(
            Module("core", ModuleType.RequiredMod),
            Module("optional", ModuleType.OptionalMod),
            Module("child", ModuleType.OptionalMod, "optional"));

        var filtered = ModuleSelection.Filter(distribution, ["optional"]);

        Assert.Equal(["core"], filtered.Modules.Select(module => module.Id));
        // The original manifest is server-owned and must not be mutated.
        Assert.Equal(3, distribution.Modules.Count);
    }

    [Fact]
    public void FilterReturnsTheSameInstanceWhenNothingIsDisabled()
    {
        var distribution = Distribution(Module("core", ModuleType.RequiredMod));

        Assert.Same(distribution, ModuleSelection.Filter(distribution, []));
        Assert.Same(distribution, ModuleSelection.Filter(distribution, null));
    }
}
