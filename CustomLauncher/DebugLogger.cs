using System;
using System.IO;

namespace CustomLauncher
{
    public static class DebugLogger
    {
        private static readonly string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rooftop_debug_log.txt");
        private static readonly object lockObj = new object();

        /// <summary>
        /// 새 프로그램을 시작할 때 로그 파일을 초기화합니다.
        /// </summary>
        public static void Init()
        {
            try
            {
                lock (lockObj)
                {
                    File.WriteAllText(logFilePath, $"-- - Log Start: {DateTime.Now} ---\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to init debug logger: " + ex.Message);
            }
        }

        /// <summary>
        /// 로그 파일에 메시지를 추가합니다.
        /// </summary>
        /// <param name="message">기록할 메시지</param>
        public static void Log(string message)
        {
            try
            {
                lock (lockObj)
                {
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to write to debug log: " + ex.Message);
            }
        }
    }
}
