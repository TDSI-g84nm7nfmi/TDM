using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TDM.Models;

namespace TDM.Services
{
    /// <summary>
    /// 下载历史记录服务。
    /// </summary>
    public static class HistoryService
    {
        private static readonly string HistoryFile =
            Path.Combine(App.DataDirectory, "history.json");

        private static readonly List<HistoryEntry> _entries = new();
        private static readonly object _lock = new();
        private const int MaxEntries = 500;

        public static IReadOnlyList<HistoryEntry> Entries
        {
            get { lock (_lock) { return _entries.ToList(); } }
        }

        public static event EventHandler? Changed;

        public static void Load()
        {
            try
            {
                lock (_lock)
                {
                    _entries.Clear();
                    if (File.Exists(HistoryFile))
                    {
                        var json = File.ReadAllText(HistoryFile);
                        var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                        if (list != null)
                        {
                            _entries.AddRange(list);
                        }
                    }
                }
                Logger.Info($"已加载 {_entries.Count} 条历史记录");
            }
            catch (Exception ex)
            {
                Logger.Error("加载历史记录失败", ex);
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(App.DataDirectory);
                List<HistoryEntry> snapshot;
                lock (_lock) { snapshot = _entries.ToList(); }
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(HistoryFile, json);
            }
            catch (Exception ex)
            {
                Logger.Error("保存历史记录失败", ex);
            }
        }

        public static void Add(HistoryEntry entry)
        {
            lock (_lock)
            {
                _entries.Insert(0, entry);
                if (_entries.Count > MaxEntries)
                {
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
                }
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Remove(HistoryEntry entry)
        {
            lock (_lock)
            {
                _entries.Remove(entry);
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
