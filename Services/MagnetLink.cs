using System;
using System.Collections.Generic;
using System.Web;

namespace TDM.Services
{
    /// <summary>
    /// magnet:?xt=urn:btih:&lt;hash&gt;&amp;dn=&lt;name&gt;&amp;tr=&lt;tracker&gt;...
    /// </summary>
    public static class MagnetLink
    {
        public class MagnetInfo
        {
            public string InfoHash { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public long ExactLength { get; set; }
            public List<string> Trackers { get; set; } = new();
            public string RawUrl { get; set; } = "";
        }

        public static MagnetInfo? Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.Trim();
            if (!trimmed.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                // 去掉 magnet: 前缀
                var queryPart = trimmed.Substring(7);
                int q = queryPart.IndexOf('?');
                string query = q >= 0 ? queryPart.Substring(q + 1) : queryPart;
                var parameters = HttpUtility.ParseQueryString(query);

                var info = new MagnetInfo { RawUrl = trimmed };
                var xt = parameters["xt"];
                if (!string.IsNullOrEmpty(xt))
                {
                    // urn:btih:<hash> 或 urn:btmh:<hash>
                    var idx = xt.LastIndexOf(':');
                    if (idx >= 0) info.InfoHash = xt.Substring(idx + 1).ToLowerInvariant();
                }
                info.DisplayName = parameters["dn"] ?? "";
                if (long.TryParse(parameters["xl"], out var xl)) info.ExactLength = xl;
                var tr = parameters.GetValues("tr");
                if (tr != null) info.Trackers.AddRange(tr);

                return string.IsNullOrEmpty(info.InfoHash) ? null : info;
            }
            catch
            {
                return null;
            }
        }

        public static string MakeMagnet(string infoHash, string? name = null, List<string>? trackers = null)
        {
            var qs = $"xt=urn:btih:{infoHash.ToLowerInvariant()}";
            if (!string.IsNullOrEmpty(name)) qs += $"&dn={Uri.EscapeDataString(name)}";
            if (trackers != null)
            {
                foreach (var t in trackers) qs += $"&tr={Uri.EscapeDataString(t)}";
            }
            return "magnet:?" + qs;
        }
    }
}
