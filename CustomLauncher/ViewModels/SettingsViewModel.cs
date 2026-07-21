using CustomLauncher.Models;

namespace CustomLauncher.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly LauncherSettings _target;
    private readonly Func<CancellationToken, Task> _save;
    private string _installPath;
    private int _resolutionWidth;
    private int _resolutionHeight;
    private bool _discordRpcEnabled;
    private string _validationMessage = string.Empty;

    public SettingsViewModel(LauncherSettings target, Func<CancellationToken, Task> save)
    {
        _target = target;
        _save = save;
        _installPath = target.InstallPath;
        _resolutionWidth = target.Resolution.Width;
        _resolutionHeight = target.Resolution.Height;
        _discordRpcEnabled = target.DiscordRpcEnabled;
        Java = new JavaConfig
        {
            ExecutablePath = target.Java.ExecutablePath,
            MinRamMb = target.Java.MinRamMb,
            MaxRamMb = target.Java.MaxRamMb,
            CustomJvmArguments = new List<string>(target.Java.CustomJvmArguments)
        };
    }

    public string InstallPath { get => _installPath; set => SetProperty(ref _installPath, value); }
    public int ResolutionWidth { get => _resolutionWidth; set => SetProperty(ref _resolutionWidth, value); }
    public int ResolutionHeight { get => _resolutionHeight; set => SetProperty(ref _resolutionHeight, value); }
    public bool DiscordRpcEnabled { get => _discordRpcEnabled; set => SetProperty(ref _discordRpcEnabled, value); }
    public JavaConfig Java { get; }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!Validate()) return false;
        Directory.CreateDirectory(InstallPath);
        _target.InstallPath = Path.GetFullPath(InstallPath);
        _target.Resolution = new Resolution(ResolutionWidth, ResolutionHeight);
        _target.DiscordRpcEnabled = DiscordRpcEnabled;
        _target.Java = Java;
        await _save(cancellationToken);
        return true;
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(InstallPath))
            return Fail("설치 경로를 입력해 주세요.");
        if (ResolutionWidth is < 640 or > 7680 || ResolutionHeight is < 480 or > 4320)
            return Fail("화면 크기가 허용 범위를 벗어났습니다.");
        if (Java.MinRamMb < 512 || Java.MaxRamMb < Java.MinRamMb)
            return Fail("Java 메모리 범위를 확인해 주세요.");
        ValidationMessage = string.Empty;
        return true;
    }

    private bool Fail(string message)
    {
        ValidationMessage = message;
        return false;
    }
}
