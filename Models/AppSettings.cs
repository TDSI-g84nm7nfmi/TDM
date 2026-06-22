using System.Collections.Generic;

namespace TDM.Models
{
    public class SniffedResource
    {
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = "file";   // image / video / audio / file
        public string Filename { get; set; } = string.Empty;
        public string TypeIcon => Type switch
        {
            "image" => "🖼️",
            "video" => "🎬",
            "audio" => "🎵",
            _ => "📄"
        };
    }

    public class AppSettings
    {
        public string Theme { get; set; } = "purple";
        public string DefaultSaveDir { get; set; } = string.Empty;
        public int DefaultThreads { get; set; } = 32;
        public int MaxRetries { get; set; } = 3;
        public bool EnableClipboard { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool NotifyFinished { get; set; } = true;
        public bool NotifyError { get; set; } = true;
        public bool AcrylicBlur { get; set; } = true;
        public int ChunkSizeKB { get; set; } = 64;
        public int ConnectionTimeoutSec { get; set; } = 15;
        public bool ScanBrowsersOnStartup { get; set; } = true;

        // 关闭行为："ask"（每次询问）/ "tray"（最小化到托盘）/ "exit"（直接退出）
        public string CloseAction { get; set; } = "ask";
    }
}
