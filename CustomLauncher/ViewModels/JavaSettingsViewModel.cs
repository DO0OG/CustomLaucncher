using CustomLauncher.Core;
using CustomLauncher.Core.Java;
using CustomLauncher.Models;

namespace CustomLauncher.ViewModels;

/// <summary>
/// Java tab: discovery, validation, optional automatic installation, and RAM limits derived from
/// the machine's actual memory rather than a fixed range.
/// </summary>
public sealed class JavaSettingsViewModel : ContentTabViewModel
{
    private readonly JavaConfig _config;
    private readonly Func<IJavaDiscovery> _discoveryFactory;
    private readonly JavaValidator _validator;
    private readonly Func<int, IProgress<JavaInstallProgress>?, CancellationToken, Task<JavaInstallResult>>? _install;
    private readonly RamRecommendation _ram;
    private string? _executablePath;
    private int _minRamMb;
    private int _maxRamMb;
    private string _jvmArgumentsText;
    private bool _autoInstallEnabled;
    private string _detectedVersion = "확인하지 않음";
    private string _argumentWarning = string.Empty;

    public JavaSettingsViewModel(
        JavaConfig config,
        ISystemMemoryProvider? memory = null,
        Func<IJavaDiscovery>? discoveryFactory = null,
        JavaValidator? validator = null,
        Func<int, IProgress<JavaInstallProgress>?, CancellationToken, Task<JavaInstallResult>>? install = null)
    {
        _config = config;
        _discoveryFactory = discoveryFactory ?? (() => JavaDiscoveryFactory.Create());
        _validator = validator ?? new JavaValidator();
        _install = install;
        _ram = RamCalculator.Calculate((memory ?? new RuntimeSystemMemoryProvider()).GetTotalMemoryBytes());
        _executablePath = config.ExecutablePath;
        _minRamMb = config.MinRamMb;
        _maxRamMb = config.MaxRamMb;
        _autoInstallEnabled = config.AutoInstallEnabled;
        _jvmArgumentsText = config.JvmArgumentsText;
        UpdateArgumentWarning();

        DetectCommand = new AsyncCommand(DetectAsync, () => !Busy, ReportError);
        ValidateCommand = new AsyncCommand(ValidateAsync, () => !Busy && !string.IsNullOrWhiteSpace(_executablePath), ReportError);
        InstallCommand = new AsyncCommand(InstallAsync, () => !Busy, ReportError);
        UseRecommendedRamCommand = new RelayCommand(ApplyRecommendedRam, () => !Busy);
    }

    public AsyncCommand DetectCommand { get; }
    public AsyncCommand ValidateCommand { get; }
    public AsyncCommand InstallCommand { get; }
    public RelayCommand UseRecommendedRamCommand { get; }

    /// <summary>Lets the view offer a file picker for a manually chosen Java binary.</summary>
    public Func<Task<string?>>? FilePicker { get; set; }
    public AsyncCommand BrowseCommand => _browseCommand ??=
        new AsyncCommand(BrowseAsync, () => !Busy && FilePicker is not null, ReportError);
    private AsyncCommand? _browseCommand;

    // RAM bounds come from the machine, not from a hard-coded range.
    public int RamMinimumBound => 512;
    public int RamMaximumBound => Math.Max(_ram.MaximumMb, 1024);
    public int RecommendedRamMb => _ram.RecommendedMb;
    public string RamAdviceText =>
        $"이 PC 권장값: 최소 {_ram.MinimumMb}MB / 권장 {_ram.RecommendedMb}MB / 최대 {_ram.MaximumMb}MB";

    public string? ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (!SetProperty(ref _executablePath, value)) return;
            DetectedVersion = "확인하지 않음";
            ValidateCommand.NotifyCanExecuteChanged();
        }
    }

    public int MinRamMb { get => _minRamMb; set => SetProperty(ref _minRamMb, value); }
    public int MaxRamMb { get => _maxRamMb; set => SetProperty(ref _maxRamMb, value); }
    public bool AutoInstallEnabled { get => _autoInstallEnabled; set => SetProperty(ref _autoInstallEnabled, value); }
    public string DetectedVersion { get => _detectedVersion; private set => SetProperty(ref _detectedVersion, value); }

    /// <summary>Non-blocking advice shown when the user hand-writes heap flags.</summary>
    public string ArgumentWarning { get => _argumentWarning; private set => SetProperty(ref _argumentWarning, value); }
    public bool HasArgumentWarning => !string.IsNullOrEmpty(ArgumentWarning);

    public string JvmArgumentsText
    {
        get => _jvmArgumentsText;
        set
        {
            if (!SetProperty(ref _jvmArgumentsText, value)) return;
            UpdateArgumentWarning();
        }
    }

    /// <summary>Copies the edited values back into the settings model.</summary>
    public void ApplyTo(JavaConfig target)
    {
        target.ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim();
        target.MinRamMb = MinRamMb;
        target.MaxRamMb = MaxRamMb;
        target.AutoInstallEnabled = AutoInstallEnabled;
        target.JvmArgumentsText = JvmArgumentsText;
    }

    public JavaConfig Source => _config;

    private void ApplyRecommendedRam()
    {
        MinRamMb = Math.Min(_ram.MinimumMb, _ram.RecommendedMb);
        MaxRamMb = _ram.RecommendedMb;
        Message = "권장 메모리 값을 적용했습니다.";
    }

    private void UpdateArgumentWarning()
    {
        var arguments = JvmArgumentsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0);
        ArgumentWarning = JavaConfig.ContainsHeapOverride(arguments)
            ? "JVM 인자에 -Xmx/-Xms가 있습니다. 위의 메모리 설정과 충돌하며 인자 쪽이 우선 적용될 수 있습니다."
            : string.Empty;
        RaisePropertyChanged(nameof(HasArgumentWarning));
    }

    private async Task BrowseAsync()
    {
        if (FilePicker is null) return;
        var path = await FilePicker();
        if (!string.IsNullOrWhiteSpace(path)) ExecutablePath = path;
    }

    private async Task DetectAsync() => await RunAsync(async token =>
    {
        Message = "시스템에서 Java를 검색하는 중...";
        string? found;
        try
        {
            found = await Task.Run(() => _discoveryFactory().ScanSystemForValidJava(), token);
        }
        catch (PlatformNotSupportedException exception)
        {
            Message = exception.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(found))
        {
            Message = "설치된 Java를 찾지 못했습니다. 자동 설치를 사용하거나 경로를 직접 지정해 주세요.";
            DetectedVersion = "찾지 못함";
            return;
        }

        ExecutablePath = found;
        await ValidateCoreAsync(token);
    });

    private async Task ValidateAsync() => await RunAsync(ValidateCoreAsync);

    private async Task ValidateCoreAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) return;
        var result = await _validator.ValidateAsync(ExecutablePath, cancellationToken: token);
        if (result.IsValid)
        {
            DetectedVersion = result.Version ?? $"Java {result.MajorVersion}";
            Message = $"Java {DetectedVersion}을(를) 확인했습니다.";
        }
        else
        {
            DetectedVersion = "유효하지 않음";
            Message = result.ErrorMessage ?? "Java 실행 파일을 확인하지 못했습니다.";
        }
    }

    private async Task InstallAsync() => await RunAsync(async token =>
    {
        if (_install is null)
        {
            Message = "이 화면에서는 자동 설치를 사용할 수 없습니다. 게임 실행 시 자동으로 설치됩니다.";
            return;
        }

        var feature = JavaProvisioningService.ParseFeatureVersion((string?)null);
        Message = $"Java {feature} 설치를 시작합니다...";
        var progress = new Progress<JavaInstallProgress>(value =>
        {
            Progress = value.TotalBytes is > 0 ? value.BytesReceived * 100.0 / value.TotalBytes.Value : 0;
            Message = value.Stage;
        });
        var result = await _install(feature, progress, token);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.ExecutablePath))
        {
            ExecutablePath = result.ExecutablePath;
            AutoInstallEnabled = true;
            await ValidateCoreAsync(token);
            Message = "Java 설치를 완료했습니다.";
        }
        else
        {
            Message = result.ErrorMessage ?? "Java 설치에 실패했습니다.";
        }
    });

    protected override void OnBusyChanged()
    {
        DetectCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        UseRecommendedRamCommand.NotifyCanExecuteChanged();
    }
}
