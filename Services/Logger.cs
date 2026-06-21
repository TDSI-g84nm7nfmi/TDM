using System;
using System.IO;
using System.Text;

namespace TDM.Services
{
    public static class Logger
    {
        private static string _logDir = "";
        private static readonly object _lock = new();

        public static void Init(string logDir)
        {
            _logDir = logDir;
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch { /* 忽略 */ }
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
        public static void Debug(string message) => Write("DEBUG", message, null);

        private static void Write(string level, string message, Exception? ex)
        {
            try
            {
                var line = new StringBuilder();
                line.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                line.Append('[').Append(level).Append("] ");
                line.AppendLine(message);
                if (ex != null)
                {
                    line.AppendLine(ex.ToString());
                }

                lock (_lock)
                {
                    if (!string.IsNullOrEmpty(_logDir))
                    {
                        var file = Path.Combine(_logDir, $"TDM-{DateTime.Now:yyyyMMdd}.log");
                        File.AppendAllText(file, line.ToString(), Encoding.UTF8);
                    }
                }
            }
            catch
            {
                // 日志失败不应影响主流程
            }
        }
    }
}
