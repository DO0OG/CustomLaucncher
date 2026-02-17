using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CustomLauncher
{
    /// <summary>
    /// AES-256 대칭키 암호화/복호화 유틸리티 클래스.
    /// 사용자 인증 토큰 파일 보호에 사용됩니다.
    /// </summary>
    public static class EncryptionHelper
    {
        // SHA256으로 해싱하여 항상 정확히 32바이트(AES-256) 키를 생성
        private static readonly byte[] Key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("비밀번호"));
        private static readonly byte[] IV = new byte[16]; // 16바이트 초기화 벡터

        /// <summary>
        /// 평문 문자열을 AES 암호화하여 바이트 배열로 반환합니다.
        /// </summary>
        /// <param name="plainText">암호화할 평문</param>
        public static byte[] Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;

                using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            using (StreamWriter sw = new StreamWriter(cs))
                            {
                                sw.Write(plainText);
                            }
                            return ms.ToArray();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// AES 암호화된 바이트 배열을 복호화하여 평문 문자열로 반환합니다.
        /// </summary>
        /// <param name="cipherText">복호화할 암호문 바이트 배열</param>
        public static string Decrypt(byte[] cipherText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Key;
                aes.IV = IV;

                using (ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                {
                    using (MemoryStream ms = new MemoryStream(cipherText))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader sr = new StreamReader(cs))
                            {
                                return sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
        }
    }
}
