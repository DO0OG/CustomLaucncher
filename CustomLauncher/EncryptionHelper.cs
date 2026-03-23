using System.Security.Cryptography;
using System.Text;

namespace CustomLauncher
{
    /// <summary>
    /// Windows DPAPI 기반 암호화/복호화 유틸리티 클래스.
    /// 키를 코드에 저장하지 않고 Windows 사용자 계정 자격증명으로 보호합니다.
    /// </summary>
    public static class EncryptionHelper
    {
        // CurrentUser: 동일 Windows 계정에서만 복호화 가능
        private static readonly DataProtectionScope Scope = DataProtectionScope.CurrentUser;

        /// <summary>
        /// 평문 문자열을 DPAPI로 암호화하여 바이트 배열로 반환합니다.
        /// </summary>
        public static byte[] Encrypt(string plainText)
        {
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            return ProtectedData.Protect(data, null, Scope);
        }

        /// <summary>
        /// DPAPI로 암호화된 바이트 배열을 복호화하여 평문 문자열로 반환합니다.
        /// </summary>
        public static string Decrypt(byte[] cipherText)
        {
            byte[] data = ProtectedData.Unprotect(cipherText, null, Scope);
            return Encoding.UTF8.GetString(data);
        }
    }
}
