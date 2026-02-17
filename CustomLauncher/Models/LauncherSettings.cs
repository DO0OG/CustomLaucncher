namespace CustomLauncher.Models
{
    /// <summary>
    /// 런처 설정 및 사용자 인증 정보를 담는 데이터 모델
    /// </summary>
    public class LauncherSettings
    {
        /// <summary>마인크래프트 계정 사용자명</summary>
        public string Username { get; set; }

        /// <summary>계정 액세스 토큰 (저장 시 AES 암호화 처리)</summary>
        public string Password { get; set; }

        /// <summary>최대/최소 RAM 할당량 (MB 단위 문자열)</summary>
        public string RamValue { get; set; }

        /// <summary>화면 해상도 문자열 (예: "1920x1080")</summary>
        public string Resolution { get; set; }

        /// <summary>마인크래프트 설치 경로</summary>
        public string InstallPath { get; set; }

        /// <summary>현재 설치된 버전 식별자</summary>
        public string VersionData { get; set; }
    }
}
