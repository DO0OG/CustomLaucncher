using System;
using System.Collections.Generic;
using System.Linq;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;

namespace CustomLauncher.Models
{
    /// <summary>
    /// Minecraft 게임 실행 옵션 모델.
    /// G1GC JVM 최적화 인수 및 Log4j2 보안 패치(Log4Shell)가 기본으로 포함됩니다.
    /// </summary>
    public class CMLaunchOption
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> EmptyDictionary =
            new Lazy<IReadOnlyDictionary<string, string>>(() => new Dictionary<string, string>());

        // 필수 파라미터
        public MinecraftPath? Path { get; set; }
        public IVersion? StartVersion { get; set; }
        public string? NativesDirectory { get; set; }
        public string? JavaPath { get; set; }
        public string PathSeparator { get; set; } = System.IO.Path.PathSeparator.ToString();

        // 선택 파라미터
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

        // QuickPlayMultiplayer 설정
        public string? ServerIp { get; set; }
        public int ServerPort { get; set; } = 25565;

        public string? ClientId { get; set; }
        public string? VersionType { get; set; }
        public string? GameLauncherName { get; set; } = "minecraft-launcher";
        public string? GameLauncherVersion { get; set; } = "2";
        public string? UserProperties { get; set; } = "{}";

        public IReadOnlyDictionary<string, string> ArgumentDictionary { get; set; } = EmptyDictionary.Value;
        public IEnumerable<MArgument>? JvmArgumentOverrides { get; set; }

        /// <summary>
        /// 기본 JVM 인수: G1GC 가비지 컬렉터 최적화 및 Log4Shell 취약점 패치 포함
        /// </summary>
        public IEnumerable<string> ExtraJvmArguments { get; set; } = new List<string>
        {
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+UseG1GC",
            "-XX:G1NewSizePercent=20",
            "-XX:G1ReservePercent=20",
            "-XX:MaxGCPauseMillis=50",
            "-XX:G1HeapRegionSize=16M",
            "-Dlog4j2.formatMsgNoLookups=true"  // Log4Shell (CVE-2021-44228) 패치
        };

        public IEnumerable<MArgument> ExtraGameArguments { get; set; } = Enumerable.Empty<MArgument>();

        /// <summary>
        /// 필수 파라미터 유효성을 검사합니다. 유효하지 않으면 예외를 발생시킵니다.
        /// </summary>
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
                throw new ArgumentException("유효하지 않은 세션입니다.");

            if (ServerPort < 0 || ServerPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(ServerPort), ServerPort, "ServerPort 유효 범위: 0 ~ 65535");

            if (ScreenWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(ScreenWidth), ScreenWidth, "음수 값은 허용되지 않습니다.");

            if (ScreenHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(ScreenHeight), ScreenHeight, "음수 값은 허용되지 않습니다.");

            if (MaximumRamMb < 0)
                throw new ArgumentOutOfRangeException(nameof(MaximumRamMb), MaximumRamMb, "음수 값은 허용되지 않습니다.");

            if (MinimumRamMb < 0)
                throw new ArgumentOutOfRangeException(nameof(MinimumRamMb), MinimumRamMb, "음수 값은 허용되지 않습니다.");

            if (MinimumRamMb > MaximumRamMb)
                throw new ArgumentOutOfRangeException(nameof(MinimumRamMb), MinimumRamMb, "MinimumRamMb는 MaximumRamMb보다 클 수 없습니다.");
        }
    }
}
