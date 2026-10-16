using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using CustomLauncher.ManifestTool.Commands;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ManifestTool;

public sealed class ManifestToolViewModel : INotifyPropertyChanged
{
    private readonly ToolSettings _settings;
    private readonly bool _persistSettings;
    private string _sourceDirectory;
    private string _urlTemplate;
    private string _outputPath;
    private bool _mergeWithExisting;
    private string _log = string.Empty;
    private bool _busy;
    private string? _defaultShaderPackId;
    private string? _knownManifestPath;
    private ServerItem? _selectedServer;

    /// <param name="persistSettings">
    /// False in tests, which must not write the operator's remembered values in AppData.
    /// </param>
    public ManifestToolViewModel(ToolSettings? settings = null, bool persistSettings = true)
    {
        _settings = settings ?? ToolSettings.Load();
        _persistSettings = persistSettings;
        _sourceDirectory = _settings.SourceDirectory;
        _urlTemplate = _settings.UrlTemplate;
        _outputPath = _settings.OutputPath;
        _mergeWithExisting = _settings.MergeWithExisting;
        Servers.CollectionChanged += (_, _) => Raise(nameof(CanDeleteServer));
        Servers.Add(new ServerItem(new ServerEntry { Id = ServerCatalog.LegacyServerId }));
        SelectedServer = Servers[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceDirectory
    {
        get => _sourceDirectory;
        set
        {
            if (!Set(ref _sourceDirectory, value)) return;
            // Sensible default so a first run needs one folder pick and one URL.
            if (string.IsNullOrWhiteSpace(OutputPath) && !string.IsNullOrWhiteSpace(value))
                OutputPath = Path.Combine(value, "distribution.json");
        }
    }

    public ObservableCollection<ServerItem> Servers { get; } = [];

    public ServerItem? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (Set(ref _selectedServer, value)) Raise(nameof(CanDeleteServer));
        }
    }

    /// <summary>The manifest must keep at least one server.</summary>
    public bool CanDeleteServer => Servers.Count > 1 && SelectedServer is not null;

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

    public void AddServer()
    {
        var number = 1;
        while (Servers.Any(server => server.Id == $"server-{number}")) number++;
        var item = new ServerItem(new ServerEntry { Id = $"server-{number}", Port = ServerCatalog.DefaultPort });
        Servers.Add(item);
        SelectedServer = item;
    }

    public void RemoveServer()
    {
        if (!CanDeleteServer) return;
        var index = Servers.IndexOf(SelectedServer!);
        Servers.RemoveAt(index);
        SelectedServer = Servers[Math.Min(index, Servers.Count - 1)];
    }

    /// <summary>Reads the manifest at the save location into the server list.</summary>
    /// <param name="startup">True for the automatic load at start: a missing file is not worth a message.</param>
    public async Task LoadAsync(bool startup = false)
    {
        Busy = true;
        try
        {
            if (!File.Exists(OutputPath))
            {
                if (!startup) Log = "저장 위치에 불러올 매니페스트가 없습니다.";
                return;
            }

            var manifest = await GenerateCommand.TryReadAsync(OutputPath);
            if (manifest is null)
            {
                Log = "매니페스트를 읽지 못했습니다: " + OutputPath;
                return;
            }

            var legacy = new ServerEntry
            {
                Id = ServerCatalog.LegacyServerId,
                Name = manifest.ServerId ?? string.Empty,
            };
            var normalized = ServerCatalog.Normalize(manifest, legacy);
            _defaultShaderPackId = normalized.DefaultShaderPackId;
            Servers.Clear();
            foreach (var server in normalized.Servers) Servers.Add(new ServerItem(server));
            SelectedServer = Servers.Count > 0 ? Servers[0] : null;
            _knownManifestPath = Path.GetFullPath(OutputPath);

            var report = new StringBuilder($"서버 {Servers.Count}개를 불러왔습니다: {OutputPath}");
            if (manifest.Servers.Count == 0)
                report.Append("\n옛 단일 서버 매니페스트입니다. 이름·주소·Minecraft 버전·로더를 채워야 저장됩니다.");
            Log = report.ToString();
        }
        catch (Exception exception)
        {
            Log = "불러오지 못했습니다: " + exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Saves the server list as it is, without scanning the folder.</summary>
    public async Task SaveAsync()
    {
        Busy = true;
        try
        {
            var output = ResolveOutput();
            if (output is null)
            {
                Log = "저장 위치를 입력해 주세요.";
                return;
            }

            Log = $"저장했습니다: {await SaveManifestAsync(output)}";
            Remember(output);
        }
        catch (Exception exception)
        {
            Log = "저장하지 않았습니다: " + exception.Message;
        }
        finally
        {
            Busy = false;
        }
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
            if (SelectedServer is null)
            {
                Log = "모듈을 생성할 서버를 선택해 주세요.";
                return;
            }

            var output = ResolveOutput()!;
            var server = SelectedServer.Entry;
            var oldModules = server.Modules;
            var options = new GenerateOptions
            {
                SourceDirectory = SourceDirectory,
                OutputPath = output,
                UrlTemplate = UrlTemplate,
            };

            var scanned = await GenerateCommand.ScanModulesAsync(options, MergeWithExisting ? oldModules : []);
            report.AppendLine($"[{server.Id}] 모듈 {scanned.Count}개를 스캔했습니다.");

            var manifest = BuildManifest();
            var before = ServerCatalog.ForServer(manifest, server);
            server.Modules = scanned;
            try
            {
                if (oldModules.Count > 0)
                {
                    var changes = DiffCommand.Compare(before, ServerCatalog.ForServer(manifest, server));
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

                var written = await SaveManifestAsync(output);
                report.AppendLine($"저장했습니다: {written}");
                report.AppendLine();
                report.AppendLine("이 파일과 배포 폴더의 파일들을 서버에 올리면 됩니다.");
            }
            catch
            {
                server.Modules = oldModules;
                throw;
            }

            Remember(output);
        }
        catch (Exception exception)
        {
            report.AppendLine("저장하지 않았습니다: " + exception.Message);
        }
        finally
        {
            if (report.Length > 0) Log = report.ToString().TrimEnd();
            Busy = false;
        }
    }

    private string? ResolveOutput()
    {
        if (!string.IsNullOrWhiteSpace(OutputPath)) return OutputPath;
        return string.IsNullOrWhiteSpace(SourceDirectory) ? null : Path.Combine(SourceDirectory, "distribution.json");
    }

    private ServerDistribution BuildManifest() => new()
    {
        // Empty on purpose: a launcher that predates servers requires a ServerId and rejects this
        // manifest, instead of reading its empty module list and deleting the player's mods.
        ServerId = string.Empty,
        Modules = [],
        Java = new JavaRequirement(),
        DefaultShaderPackId = _defaultShaderPackId,
        Servers = [.. Servers.Select(item => item.Entry)],
    };

    private async Task<string> SaveManifestAsync(string output)
    {
        var manifest = BuildManifest();
        ServerCatalog.Validate(manifest);
        var fullPath = Path.GetFullPath(output);
        // The list in memory came from another file; overwriting this one would drop its servers.
        if (File.Exists(fullPath) && !string.Equals(fullPath, _knownManifestPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("저장 위치에 다른 매니페스트가 있습니다. 먼저 '불러오기'로 연 뒤 저장해 주세요.");
        var written = await GenerateCommand.SaveAsync(manifest, output);
        _knownManifestPath = fullPath;
        return written;
    }

    private void Remember(string output)
    {
        _settings.SourceDirectory = SourceDirectory;
        _settings.UrlTemplate = UrlTemplate;
        _settings.OutputPath = output;
        _settings.MergeWithExisting = MergeWithExisting;
        if (_persistSettings) _settings.Save();
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
