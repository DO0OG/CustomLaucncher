using CustomLauncher.Models;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ChangesAreCommittedOnlyAfterSuccessfulSave()
    {
        var settings = new LauncherSettings { InstallPath = Path.GetTempPath() };
        var saves = 0;
        var viewModel = new SettingsViewModel(settings, _ => { saves++; return Task.CompletedTask; });
        var changed = Path.Combine(Path.GetTempPath(), $"launcher-settings-{Guid.NewGuid():N}");
        viewModel.InstallPath = changed;
        viewModel.ResolutionWidth = 1920;

        Assert.NotEqual(changed, settings.InstallPath);
        Assert.True(await viewModel.SaveAsync());
        Assert.Equal(changed, settings.InstallPath);
        Assert.Equal(1920, settings.Resolution.Width);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task InvalidMemoryRangeDoesNotMutateOrSave()
    {
        var settings = new LauncherSettings { InstallPath = Path.GetTempPath() };
        var saves = 0;
        var viewModel = new SettingsViewModel(settings, _ => { saves++; return Task.CompletedTask; });
        viewModel.Java.MinRamMb = 4096;
        viewModel.Java.MaxRamMb = 2048;

        Assert.False(await viewModel.SaveAsync());
        Assert.Equal(0, saves);
        Assert.Equal(2048, settings.Java.MinRamMb);
    }
}
