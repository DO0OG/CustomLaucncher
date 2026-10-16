using CustomLauncher.Core;
using CustomLauncher.Core.Java;
using CustomLauncher.Models;
using CustomLauncher.Shared.Models;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

public sealed class JavaSettingsInstallVersionTests
{
    private sealed class FixedMemory : ISystemMemoryProvider
    {
        public long GetTotalMemoryBytes() => 8L * 1024 * 1024 * 1024;
    }

    [Fact]
    public async Task InstallUsesTheVersionFromTheRequirementSource()
    {
        var requested = new TaskCompletionSource<int>();
        var viewModel = new JavaSettingsViewModel(
            new JavaConfig(),
            new FixedMemory(),
            install: (feature, _, _) =>
            {
                requested.SetResult(feature);
                return Task.FromResult(JavaInstallResult.Failure("test"));
            },
            requirementSource: _ => Task.FromResult<JavaRequirement?>(new JavaRequirement { RecommendedVersion = "21" }));

        viewModel.InstallCommand.Execute(null);

        Assert.Equal(21, await requested.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
