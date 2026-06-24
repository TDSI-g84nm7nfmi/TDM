using System;
using System.Collections.Generic;
using System.Web;

namespace TDM.Services
{
    /// <summary>
    /// ed2k://|file|&lt;name&gt;|&lt;size&gt;|&lt;hash&gt;|/
    ///   或 ed2k://|file|&lt;name&gt;|&lt;size&gt;|&lt;hash&gt;|p=&lt;sources&gt;|/
    ///   或 ed2k://|server|&lt;ip&gt;|&lt;port&gt;|/
    ///   或 ed2k://|friend|&lt;id&gt;|&lt;name&gt;|/
    /// </summary>
    public static class Ed2kLink
    {
        public enum Ed2kType { File, Server, Friend, Sources, Unknown }

        public class Ed2kFileInfo
        {
            public Ed2kType Type { get; set; } = Ed2kType.File;
            public string Name { get; set; } = "";
            public long Size { get; set; }
            public string Hash { get; set; } = "";       // MD4 16 字节十六进制
            public string AichHash { get; set; } = "";
            public string Sources { get; set; } = "";     // ed2k sources 列表
            public string? Server { get; set; }
            public string? Port { get; set; }
            public string RawUrl { get; set; } = "";
        }

        public static Ed2kFileInfo? Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.Trim();
            if (!trimmed.StartsWith("ed2k://", StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                // 去掉 ed2k:// 前缀
                var body = trimmed.Substring(7);
                // 拆分 |
                var parts = body.Split('|', StringSplitOptions.None);
                if (parts.Length == 0) return null;

                // 第一个是 type（可能前面有 "|" 残留为空段）
                int cursor = 0;
                if (parts[0].Length == 0) cursor = 1;

                var info = new Ed2kFileInfo { RawUrl = trimmed };
                var typeText = parts[cursor].ToLowerInvariant();
                switch (typeText)
                {
                    case "file":
                        info.Type = Ed2kType.File;
                        if (cursor + 4 < parts.Length)
                        {
                            info.Name = Uri.UnescapeDataString(parts[cursor + 1]);
                            if (long.TryParse(parts[cursor + 2], out var sz)) info.Size = sz;
                            info.Hash = parts[cursor + 3].ToLowerInvariant();
                            // 可选 p= (sources)
                            if (cursor + 4 < parts.Length && parts[cursor + 4].StartsWith("p=", StringComparison.OrdinalIgnoreCase))
                            {
                                info.Sources = parts[cursor + 4].Substring(2);
                            }
                        }
                        break;
                    case "server":
                        info.Type = Ed2kType.Server;
                        if (cursor + 2 < parts.Length)
                        {
                            info.Server = parts[cursor + 1];
                            info.Port = parts[cursor + 2];
                        }
                        break;
                    case "friend":
                        info.Type = Ed2kType.Friend;
                        if (cursor + 2 < parts.Length)
                        {
                            info.Hash = parts[cursor + 1];
                            info.Name = Uri.UnescapeDataString(parts[cursor + 2]);
                        }
                        break;
                    default:
                        info.Type = Ed2kType.Unknown;
                        break;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        public static string MakeEd2k(string name, long size, string md4Hash, string? sources = null)
        {
            var url = $"ed2k://|file|{Uri.EscapeDataString(name)}|{size}|{md4Hash.ToLowerInvariant()}|/";
            if (!string.IsNullOrEmpty(sources)) url = url.Replace("|/", $"|p={sources}|/");
            return url;
        }
    }
}
