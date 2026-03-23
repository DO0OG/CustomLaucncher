using System;
using System.IO;

namespace CustomLauncher
{
    public static class DebugLogger
    {
        private static readonly string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LauncherConfig.DebugLogFileName);
        private static readonly object lockObj = new object();

        /// <summary>
        /// ???�로그램???�작????로그 ?�일??초기?�합?�다.
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
        /// 로그 ?�일??메시지�?추�??�니??
        /// </summary>
        /// <param name="message">기록??메시지</param>
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
