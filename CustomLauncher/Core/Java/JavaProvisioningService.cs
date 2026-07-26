using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core.Java;

public interface IJavaProvisioner
{
    Task<string?> EnsureAsync(JavaRequirement requirement, MLaunchOption launchOption,
        bool allowInstall, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
}

public sealed class JavaProvisioningService : IJavaProvisioner, IDisposable
{
    private readonly IJavaDiscovery _discovery;
    private readonly JavaValidator _validator;
    private readonly JavaInstaller _installer;
    private readonly HttpClient? _ownedClient;

    public JavaProvisioningService(string runtimeRoot, IJavaDiscovery? discovery = null,
        JavaValidator? validator = null, JavaInstaller? installer = null)
    {
        _discovery = discovery ?? JavaDiscoveryFactory.Create();
        _validator = validator ?? new JavaValidator();
        if (installer is null)
        {
            _ownedClient = new HttpClient();
            _installer = new JavaInstaller(_ownedClient, runtimeRoot, _validator);
        }
        else _installer = installer;
    }

    public async Task<string?> EnsureAsync(JavaRequirement requirement, MLaunchOption launchOption,
        bool allowInstall, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var required = ParseFeatureVersion(requirement.RecommendedVersion, requirement.MinVersion);
        var candidate = launchOption.JavaPath;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var configured = await _validator.ValidateAsync(candidate, required, cancellationToken);
            if (configured.IsValid) return candidate;
        }

        progress?.Report(new LaunchProgress("Java 검색", 0.1));
        candidate = _discovery.ScanSystemForValidJava();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var discovered = await _validator.ValidateAsync(candidate, required, cancellationToken);
            if (discovered.IsValid)
            {
                launchOption.JavaPath = candidate;
                return candidate;
            }
        }

        if (!allowInstall)
            throw new InvalidOperationException($"Java {required}이 필요합니다. 설정에서 자동 설치에 동의하거나 Java 경로를 지정해 주세요.");

        progress?.Report(new LaunchProgress("Java 설치", 0.2));
        var installProgress = new Progress<JavaInstallProgress>(value =>
        {
            var ratio = value.TotalBytes is > 0 ? value.BytesReceived / (double)value.TotalBytes.Value : 0.5;
            progress?.Report(new LaunchProgress("Java 설치", ratio, value.Stage));
        });
        var installed = await _installer.InstallAsync(required, installProgress, cancellationToken);
        if (!installed.Succeeded || string.IsNullOrWhiteSpace(installed.ExecutablePath))
            throw new InvalidOperationException(installed.ErrorMessage ?? "Java 설치에 실패했습니다.");
        launchOption.JavaPath = installed.ExecutablePath;
        return installed.ExecutablePath;
    }

    public static int ParseFeatureVersion(params string?[] values)
    {
        foreach (var value in values)
        {
            var digits = new string((value ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var version) && version > 0) return version == 1 ? 8 : version;
        }
        return 17;
    }

    public void Dispose() => _ownedClient?.Dispose();
}
