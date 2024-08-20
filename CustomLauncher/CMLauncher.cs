using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;

namespace CustomLauncher
{
    public class FileChangedEventArgs : EventArgs
    {
        public string FileKind { get; set; }
        public string FileName { get; set; }
        public int ProgressedFileCount { get; set; }
        public int TotalFileCount { get; set; }
    }

    public class ProgressChangedEventArgs : EventArgs
    {
        public int ProgressPercentage { get; set; }
    }

    public class CMLaunchOption
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> EmptyDictionary =
            new Lazy<IReadOnlyDictionary<string, string>>(() => new Dictionary<string, string>());

        // required parameters
        public MinecraftPath? Path { get; set; }
        public IVersion? StartVersion { get; set; }
        public string? NativesDirectory { get; set; }
        public string? JavaPath { get; set; }
        public string PathSeparator { get; set; } = System.IO.Path.PathSeparator.ToString();

        // optional parameters
        public MSession? Session { get; set; }
        public IEnumerable<string> Features { get; set; } = null;

        public int MaximumRamMb { get; set; }
        public int MinimumRamMb { get; set; }
        public string? DockName { get; set; }
        public string? DockIcon { get; set; }

        public bool IsDemo { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public bool FullScreen { get; set; }
        public string? QuickPlayPath { get; set; }
        public string? QuickPlaySingleplayer { get; set; }
        public string? QuickPlayRealms { get; set; }

        // QuickPlayMultiplayer
        public string? ServerIp { get; set; }
        public int ServerPort { get; set; } = 25565;

        public string? ClientId { get; set; }
        public string? VersionType { get; set; }
        public string? GameLauncherName { get; set; } = "minecraft-launcher";
        public string? GameLauncherVersion { get; set; } = "2";
        public string? UserProperties { get; set; } = "{}";

        public IReadOnlyDictionary<string, string> ArgumentDictionary { get; set; } = EmptyDictionary.Value;
        public IEnumerable<MArgument>? JvmArgumentOverrides { get; set; }
        public IEnumerable<string> ExtraJvmArguments { get; set; } = new List<string>
        {
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MaxGCPauseMillis=50",
            "-XX:G1HeapRegionSize=16M",
            "-Dlog4j2.formatMsgNoLookups=true"
        };
        public IEnumerable<MArgument> ExtraGameArguments { get; set; } = Enumerable.Empty<MArgument>();

        internal void CheckValid()
        {
            if (string.IsNullOrEmpty(JavaPath))
                throw new ArgumentNullException(nameof(JavaPath));

            if (Path == null)
                throw new ArgumentNullException(nameof(Path));

            if (StartVersion == null)
                throw new ArgumentNullException(nameof(StartVersion));

            if (Session == null)
                Session = MSession.CreateOfflineSession("tester123");

            if (!Session.CheckIsValid())
                throw new ArgumentException("Invalid session");

            if (ServerPort < 0 || ServerPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(ServerPort), ServerPort, "Valid range of ServerPort is 0 ~ 65535.");

            if (ScreenWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(ScreenWidth), ScreenWidth, "Cannot be a negative value.");

            if (ScreenHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(ScreenHeight), ScreenHeight, "Cannot be a negative value.");

            if (MaximumRamMb < 0)
                throw new ArgumentOutOfRangeException(nameof(MaximumRamMb), MaximumRamMb, "Cannot be a negative value.");

            if (MinimumRamMb < 0)
                throw new ArgumentOutOfRangeException(nameof(MinimumRamMb), MinimumRamMb, "Cannot be a negative value.");

            if (MinimumRamMb > MaximumRamMb)
                throw new ArgumentOutOfRangeException(nameof(MinimumRamMb), MinimumRamMb, "MinimumRamMb cannot be greater than MaximumRamMb.");
        }
    }

    internal class CMLauncher
    {
        private MinecraftPath _path;

        public CMLauncher(MinecraftPath path)
        {
            _path = path;
        }

        public event EventHandler<FileChangedEventArgs> FileChanged;

        public event EventHandler<ProgressChangedEventArgs> ProgressChanged;

        public async Task<List<MinecraftVersion>> GetAllVersionsAsync()
        {
            await Task.Delay(1000);
            return new List<MinecraftVersion>
            {
            };
        }
    }

    // Minecraft 버전 정의
    public class MinecraftVersion
    {
        public string Name { get; set; }
    }
}
