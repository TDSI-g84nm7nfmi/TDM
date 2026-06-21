using System;

namespace TDM.Models
{
    public class HistoryEntry
    {
        public string Url { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Status { get; set; } = "完成";
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public string? Error { get; set; }

        public string FileName => string.IsNullOrEmpty(FilePath)
            ? Url
            : System.IO.Path.GetFileName(FilePath);

        public string StartTimeText => StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string EndTimeText => EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
    }
}
