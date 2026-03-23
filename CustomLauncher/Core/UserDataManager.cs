using System;
using System.IO;
using System.Windows.Forms;
using CustomLauncher;
using CustomLauncher.Models;

namespace CustomLauncher.Core
{
    /// <summary>
    /// 사용자 인증 정보의 AES 암호화 저장 및 로드를 담당하는 서비스 클래스.
    /// 데이터는 EncryptionHelper를 통해 암호화되어 AppData 폴더에 저장됩니다.
    /// </summary>
    public static class UserDataManager
    {
        /// <summary>암호화된 사용자 데이터 파일 경로 (AppData 폴더)</summary>
        private static readonly string UserDataFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LauncherConfig.UserDataFileName);

        /// <summary>
        /// 암호화된 사용자 데이터 파일에서 인증 정보를 불러옵니다.
        /// </summary>
        /// <returns>사용자명과 액세스 토큰이 담긴 LauncherSettings 객체</returns>
        public static LauncherSettings Load()
        {
            var settings = new LauncherSettings();

            if (!File.Exists(UserDataFilePath))
                return settings;

            try
            {
                var encryptedData = File.ReadAllBytes(UserDataFilePath);
                var decryptedData = EncryptionHelper.Decrypt(encryptedData);
                var lines = decryptedData.Split('\n');

                if (lines.Length >= 2)
                {
                    settings.Username = lines[0];
                    settings.Password = lines[1];
                }
            }
            catch
            {
                // 복호화 실패 = 구형 형식 또는 손상된 파일 → 삭제 후 재로그인 유도
                File.Delete(UserDataFilePath);
            }

            return settings;
        }

        /// <summary>
        /// 사용자 인증 정보를 AES 암호화하여 파일에 저장합니다.
        /// </summary>
        /// <param name="username">마인크래프트 계정 사용자명</param>
        /// <param name="accessToken">계정 액세스 토큰</param>
        public static void Save(string username, string accessToken)
        {
            try
            {
                var data = $"{username}\n{accessToken}";
                var encryptedData = EncryptionHelper.Encrypt(data);
                File.WriteAllBytes(UserDataFilePath, encryptedData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"사용자 데이터 저장 실패: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
