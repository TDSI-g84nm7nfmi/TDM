using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TDM.Models;

namespace TDM.Services
{
    /// <summary>
    /// 应用程序设置服务。
    /// </summary>
    public static class SettingsService
    {
        private static readonly string SettingsFile =
            Path.Combine(App.ConfigDirectory, "settings.json");

        private static AppSettings _current = new();
        private static readonly object _lock = new();

        public static AppSettings Current
        {
            get { lock (_lock) { return _current; } }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    _current = new AppSettings
                    {
                        DefaultSaveDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "TDM", "Downloads")
                    };
                    Save();
                    return;
                }

                var json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (string.IsNullOrEmpty(loaded.DefaultSaveDir))
                {
                    loaded.DefaultSaveDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "TDM", "Downloads");
                }
                Directory.CreateDirectory(loaded.DefaultSaveDir);

                _current = loaded;
                Logger.Info($"已加载设置：{SettingsFile}");
            }
            catch (Exception ex)
            {
                Logger.Error("加载设置失败，使用默认设置", ex);
                _current = new AppSettings
                {
                    DefaultSaveDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "TDM", "Downloads")
                };
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(App.ConfigDirectory);
                var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                lock (_lock)
                {
                    File.WriteAllText(SettingsFile, json);
                }
                Logger.Info("设置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("保存设置失败", ex);
            }
        }

        public static void Update(Action<AppSettings> mutator)
        {
            lock (_lock)
            {
                mutator(_current);
            }
        }
    }
}
