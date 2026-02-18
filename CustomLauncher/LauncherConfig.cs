namespace CustomLauncher
{
    /// <summary>
    /// 런처 전체에서 사용하는 유동적인 설정값을 한 곳에서 관리합니다.
    /// 서버 주소, 파일명, 버전 정보 등을 새 런처 제작 시 여기서만 수정하면 됩니다.
    /// </summary>
    public static class LauncherConfig
    {
        // ──────────────────────────────────────────────────────────────
        // 게임 버전
        // ──────────────────────────────────────────────────────────────

        /// <summary>마인크래프트 버전 (예: "1.20.1")</summary>
        public const string McVersion = "버전";

        /// <summary>Forge 버전 (예: "47.3.0")</summary>
        public const string ForgeVersion = "버전";

        // ──────────────────────────────────────────────────────────────
        // 서버 주소
        // ──────────────────────────────────────────────────────────────

        /// <summary>마인크래프트 서버 IP 또는 도메인 (예: "play.example.com")</summary>
        public const string ServerIp = "서버IP주소";

        /// <summary>마인크래프트 서버 포트</summary>
        public const int ServerPort = 25565;

        // ──────────────────────────────────────────────────────────────
        // 원격 URL
        // ──────────────────────────────────────────────────────────────

        /// <summary>자동 업데이트 매니페스트 JSON 다운로드 URL</summary>
        public const string ManifestUrl = "Manifest주소";

        /// <summary>서버 상태 확인 API URL (mcsrvstat.us v3 기반)</summary>
        public const string ServerStatusApiUrl = "https://api.mcsrvstat.us/3/" + ServerIp;

        // ──────────────────────────────────────────────────────────────
        // AppData 파일명
        // ──────────────────────────────────────────────────────────────

        /// <summary>런처 설정 파일명 (%AppData%에 저장)</summary>
        public const string SettingsFileName = "customServer_settings.txt";

        /// <summary>암호화된 사용자 인증 데이터 파일명 (%AppData%에 저장)</summary>
        public const string UserDataFileName = "customServer_udata";

        /// <summary>디버그 로그 파일명 (%AppData%에 저장)</summary>
        public const string DebugLogFileName = "customServer_debug_log.txt";

        /// <summary>7-Zip 임시 폴더명 (%AppData%에 저장)</summary>
        public const string SevenZipTempFolder = "7Ziptemp";

        // ──────────────────────────────────────────────────────────────
        // 기본 설치 경로
        // ──────────────────────────────────────────────────────────────

        /// <summary>마인크래프트 기본 설치 폴더명 (%AppData%\{DefaultInstallFolderName})</summary>
        public const string DefaultInstallFolderName = ".custom";

        // ──────────────────────────────────────────────────────────────
        // 런처 식별 정보
        // ──────────────────────────────────────────────────────────────

        /// <summary>게임 실행 시 런처 이름 (버전 정보에 표시됨)</summary>
        public const string GameLauncherName = "SERVER";
    }
}
