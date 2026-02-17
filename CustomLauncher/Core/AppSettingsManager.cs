using System;
using System.IO;
using System.Windows.Forms;
using CustomLauncher.Models;

namespace CustomLauncher.Core
{
    /// <summary>
    /// 런처 설정 파일의 저장 및 로드를 담당하는 서비스 클래스.
    /// 설정은 AppData 폴더의 텍스트 파일에 한 줄씩 저장됩니다.
    /// </summary>
    public static class AppSettingsManager
    {
        /// <summary>설정 파일 경로 (AppData 폴더)</summary>
        public static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "customServer_settings.txt");

        /// <summary>
        /// 설정 파일에서 런처 설정을 불러옵니다.
        /// 파일이 없거나 읽기 실패 시 빈 LauncherSettings 객체를 반환합니다.
        /// </summary>
        public static LauncherSettings Load()
        {
            var settings = new LauncherSettings();

            if (!File.Exists(SettingsFilePath))
                return settings;

            try
            {
                var lines = File.ReadAllLines(SettingsFilePath);
                if (lines.Length >= 3)
                {
                    settings.Resolution = lines[0];
                    settings.InstallPath = lines[1];
                    settings.RamValue = lines[2];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return settings;
        }

        /// <summary>
        /// 런처 설정을 파일에 저장합니다.
        /// </summary>
        /// <param name="resolution">화면 해상도 문자열 (예: "1920x1080")</param>
        /// <param name="installPath">마인크래프트 설치 경로</param>
        /// <param name="ramValue">RAM 할당량 (MB 단위)</param>
        public static void Save(string resolution, string installPath, string ramValue)
        {
            try
            {
                File.WriteAllLines(SettingsFilePath, new[] { resolution, installPath, ramValue });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
