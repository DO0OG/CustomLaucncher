using CustomLauncher.Core;
using CustomLauncher.Models;

namespace CustomLauncher.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly LauncherSettings _target;
    private readonly Func<CancellationToken, Task> _save;
    private readonly HttpClient? _ownedHttpClient;
    private string _installPath;
    private int _resolutionWidth;
    private int _resolutionHeight;
    private bool _discordRpcEnabled;
    private string _validationMessage = string.Empty;

    public SettingsViewModel(LauncherSettings target, Func<CancellationToken, Task> save, AppPaths? paths = null)
    {
        _target = target;
        _save = save;
        _installPath = target.InstallPath;
        _resolutionWidth = target.Resolution.Width;
        _resolutionHeight = target.Resolution.Height;
        _discordRpcEnabled = target.DiscordRpcEnabled;
        Java = new JavaSettingsViewModel(target.Java);

        if (paths is null) return;

        // Content tabs need a live game directory; they are only built for the real app.
        var installPath = string.IsNullOrWhiteSpace(target.InstallPath) ? paths.DefaultGameDir : target.InstallPath;
        var editor = new OptionsTextEditor();
        _ownedHttpClient = new HttpClient();
        _ownedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherConfig.UserAgent);

        Modules = new ModuleManagementViewModel(
            new ContentUpdateService(_ownedHttpClient, installPath),
            new DropInModManager(Path.Combine(installPath, LauncherConfig.ModsFolderName)),
            new ModuleManagementViewModel.ModuleSelectionStore(
                () => _target.DisabledOptionalModuleIds,
                async token => { await SaveAsync(token); }));

        Shaders = new ShaderPackViewModel(new ShaderPackManager(
            Path.Combine(installPath, LauncherConfig.ShaderPackFolderName),
            Path.Combine(installPath, LauncherConfig.ShaderSettingsFileName),
            LauncherConfig.ShaderSettingsKey,
            editor));

        ResourcePacks = new ResourcePackViewModel(new ResourcePackManager(
            Path.Combine(installPath, LauncherConfig.ResourcePackFolderName),
            Path.Combine(installPath, LauncherConfig.GameOptionsFileName),
            editor));
    }

    public string InstallPath { get => _installPath; set => SetProperty(ref _installPath, value); }
    public int ResolutionWidth { get => _resolutionWidth; set => SetProperty(ref _resolutionWidth, value); }
    public int ResolutionHeight { get => _resolutionHeight; set => SetProperty(ref _resolutionHeight, value); }
    public bool DiscordRpcEnabled { get => _discordRpcEnabled; set => SetProperty(ref _discordRpcEnabled, value); }
    public JavaSettingsViewModel Java { get; }
    public ModuleManagementViewModel? Modules { get; }
    public ShaderPackViewModel? Shaders { get; }
    public ResourcePackViewModel? ResourcePacks { get; }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        // Validate before touching the model so a rejected save leaves settings untouched.
        if (!Validate()) return false;
        Directory.CreateDirectory(InstallPath);
        _target.InstallPath = Path.GetFullPath(InstallPath);
        _target.Resolution = new Resolution(ResolutionWidth, ResolutionHeight);
        _target.DiscordRpcEnabled = DiscordRpcEnabled;
        Java.ApplyTo(_target.Java);
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
            return Fail("Java 메모리 범위를 확인해 주세요. 최대값은 최소값보다 커야 합니다.");
        ValidationMessage = string.Empty;
        return true;
    }

    private bool Fail(string message)
    {
        ValidationMessage = message;
        return false;
    }

    public void Dispose()
    {
        Modules?.Dispose();
        Shaders?.Dispose();
        ResourcePacks?.Dispose();
        Java.Dispose();
        _ownedHttpClient?.Dispose();
    }
}
