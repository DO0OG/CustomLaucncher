using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using CustomLauncher.ManifestTool.Commands;

namespace CustomLauncher.ManifestTool;

public sealed class ManifestToolViewModel : INotifyPropertyChanged
{
    private readonly ToolSettings _settings;
    private readonly bool _persistSettings;
    private string _sourceDirectory;
    private string _serverId;
    private string _urlTemplate;
    private string _outputPath;
    private bool _mergeWithExisting;
    private string _log = string.Empty;
    private bool _busy;

    /// <param name="persistSettings">
    /// False in tests, which must not write the operator's remembered values in AppData.
    /// </param>
    public ManifestToolViewModel(ToolSettings? settings = null, bool persistSettings = true)
    {
        _settings = settings ?? ToolSettings.Load();
        _persistSettings = persistSettings;
        _sourceDirectory = _settings.SourceDirectory;
        _serverId = _settings.ServerId;
        _urlTemplate = _settings.UrlTemplate;
        _outputPath = _settings.OutputPath;
        _mergeWithExisting = _settings.MergeWithExisting;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceDirectory
    {
        get => _sourceDirectory;
        set
        {
            if (!Set(ref _sourceDirectory, value)) return;
            // Sensible defaults so a first run needs one folder pick and one URL.
            if (string.IsNullOrWhiteSpace(ServerId) && !string.IsNullOrWhiteSpace(value))
                ServerId = new DirectoryInfo(value).Name;
            if (string.IsNullOrWhiteSpace(OutputPath) && !string.IsNullOrWhiteSpace(value))
                OutputPath = Path.Combine(value, "distribution.json");
        }
    }

    public string ServerId { get => _serverId; set => Set(ref _serverId, value); }
    public string UrlTemplate { get => _urlTemplate; set => Set(ref _urlTemplate, value); }
    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public bool MergeWithExisting { get => _mergeWithExisting; set => Set(ref _mergeWithExisting, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) Raise(nameof(CanGenerate));
        }
    }

    public bool CanGenerate => !Busy;

    /// <summary>Picks the folder for the operator; supplied by the window.</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    public async Task BrowseAsync()
    {
        if (FolderPicker is null) return;
        var folder = await FolderPicker();
        if (!string.IsNullOrWhiteSpace(folder)) SourceDirectory = folder;
    }

    public async Task GenerateAsync()
    {
        Busy = true;
        var report = new StringBuilder();
        try
        {
            if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
            {
                Log = "배포 폴더를 선택해 주세요.";
                return;
            }
            if (string.IsNullOrWhiteSpace(UrlTemplate))
            {
                Log = "다운로드 URL 형식을 입력해 주세요.";
                return;
            }
            if (!UrlTemplate.Contains(GenerateCommand.PathPlaceholder, StringComparison.Ordinal))
            {
                Log = $"URL 형식에 {GenerateCommand.PathPlaceholder} 가 있어야 합니다. " +
                      "그 자리에 각 파일의 경로가 들어갑니다.";
                return;
            }

            var output = string.IsNullOrWhiteSpace(OutputPath)
                ? Path.Combine(SourceDirectory, "distribution.json")
                : OutputPath;
            var previous = MergeWithExisting ? await GenerateCommand.TryReadAsync(output) : null;

            var options = new GenerateOptions
            {
                SourceDirectory = SourceDirectory,
                OutputPath = output,
                ExistingPath = MergeWithExisting && File.Exists(output) ? output : null,
                ServerId = string.IsNullOrWhiteSpace(ServerId) ? null : ServerId,
                UrlTemplate = UrlTemplate,
            };

            var distribution = await GenerateCommand.GenerateAsync(options);
            report.AppendLine($"모듈 {distribution.Modules.Count}개를 스캔했습니다.");

            if (previous is not null)
            {
                var changes = DiffCommand.Compare(previous, distribution);
                if (changes.Count == 0)
                {
                    report.AppendLine("이전 매니페스트와 달라진 점이 없습니다.");
                }
                else
                {
                    report.AppendLine($"변경 {changes.Count}건:");
                    foreach (var change in changes.Take(50)) report.AppendLine("  " + change);
                    if (changes.Count > 50) report.AppendLine($"  … 외 {changes.Count - 50}건");
                }
            }

            var written = await GenerateCommand.SaveAsync(distribution, output);
            report.AppendLine($"저장했습니다: {written}");
            report.AppendLine();
            report.AppendLine("이 파일과 배포 폴더의 파일들을 서버에 올리면 됩니다.");

            _settings.SourceDirectory = SourceDirectory;
            _settings.ServerId = ServerId;
            _settings.UrlTemplate = UrlTemplate;
            _settings.OutputPath = output;
            _settings.MergeWithExisting = MergeWithExisting;
            if (_persistSettings) _settings.Save();
        }
        catch (Exception exception)
        {
            report.AppendLine("실패했습니다: " + exception.Message);
        }
        finally
        {
            if (report.Length > 0) Log = report.ToString().TrimEnd();
            Busy = false;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
