using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TDM.Services
{
    /// <summary>
    /// BEP-3 bencode 解析器，用于解析 .torrent 文件。
    /// 支持类型：int (i..e)、string (len:..)、list (l..e)、dict (d..e)。
    /// </summary>
    public static class BencodeParser
    {
        public static object? Parse(byte[] data) => Parse(new MemoryStream(data, writable: false));

        public static object? Parse(Stream input)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            return ParseNext(reader);
        }

        public static object? ParseFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Parse(fs);
        }

        private static object ParseNext(BinaryReader r)
        {
            int b = PeekByte(r);
            if (b < 0) throw new EndOfStreamException("bencode 数据意外结束");
            if (b == 'i') return ParseInt(r);
            if (b == 'l') return ParseList(r);
            if (b == 'd') return ParseDict(r);
            if (b >= '0' && b <= '9') return ParseString(r);
            throw new FormatException($"bencode 解析失败：意外的字节 0x{b:X2} ('{(char)b}')");
        }

        private static int PeekByte(BinaryReader r)
        {
            var stream = r.BaseStream;
            if (!stream.CanSeek)
            {
                int b = r.ReadByte();
                if (b < 0) return b;
                var buffer = new byte[1] { (byte)b };
                stream.Position -= 1;
                return b;
            }
            long pos = stream.Position;
            int result = r.ReadByte();
            stream.Position = pos;
            return result;
        }

        private static long ParseInt(BinaryReader r)
        {
            // i<digits>e
            int b = r.ReadByte();
            if (b != 'i') throw new FormatException("预期 'i'");

            bool negative = false;
            int first = r.ReadByte();
            if (first == '-') { negative = true; first = r.ReadByte(); }

            long value = 0;
            if (first < '0' || first > '9') throw new FormatException("非法的整数起始字符");
            value = first - '0';
            while (true)
            {
                int c = r.ReadByte();
                if (c == 'e') break;
                if (c < '0' || c > '9') throw new FormatException("非法的整数字符");
                value = value * 10 + (c - '0');
            }
            return negative ? -value : value;
        }

        private static byte[] ParseString(BinaryReader r)
        {
            // len:string
            int len = 0;
            while (true)
            {
                int c = r.ReadByte();
                if (c == ':') break;
                if (c < '0' || c > '9') throw new FormatException("非法的字符串长度字符");
                len = len * 10 + (c - '0');
            }
            return r.ReadBytes(len);
        }

        private static List<object?> ParseList(BinaryReader r)
        {
            int b = r.ReadByte();
            if (b != 'l') throw new FormatException("预期 'l'");
            var list = new List<object?>();
            while (true)
            {
                int p = PeekByte(r);
                if (p < 0) throw new EndOfStreamException("bencode list 意外结束");
                if (p == 'e') { r.ReadByte(); break; }
                list.Add(ParseNext(r));
            }
            return list;
        }

        private static Dictionary<string, object?> ParseDict(BinaryReader r)
        {
            int b = r.ReadByte();
            if (b != 'd') throw new FormatException("预期 'd'");
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (true)
            {
                int p = PeekByte(r);
                if (p < 0) throw new EndOfStreamException("bencode dict 意外结束");
                if (p == 'e') { r.ReadByte(); break; }
                var keyBytes = ParseString(r);
                var key = Encoding.UTF8.GetString(keyBytes);
                var value = ParseNext(r);
                dict[key] = value;
            }
            return dict;
        }

        /// <summary>
        /// 把 bencode 字典递归转成强类型对象。
        /// </summary>
        public static TorrentMetadata? ToTorrentMetadata(Dictionary<string, object?> dict)
        {
            if (!dict.TryGetValue("info", out var infoObj) || infoObj is not Dictionary<string, object?> info)
                throw new FormatException("种子缺少 info 字典");

            string name = AsString(info, "name") ?? "unknown";
            long pieceLength = AsLong(info, "piece length");
            if (pieceLength <= 0) throw new FormatException("非法的 piece length");

            // pieces: 20 字节倍数（SHA1）
            var piecesBytes = AsBytes(info, "pieces") ?? Array.Empty<byte>();
            int pieceCount = piecesBytes.Length / 20;

            // 单文件 vs 多文件
            bool isMulti = info.ContainsKey("files");
            long totalSize = 0;
            var files = new List<TorrentFileInfo>();

            if (isMulti && info["files"] is List<object?> fileList)
            {
                foreach (var f in fileList)
                {
                    if (f is not Dictionary<string, object?> fd) continue;
                    long length = AsLong(fd, "length");
                    var pathParts = new List<string>();
                    if (fd.TryGetValue("path", out var pathObj) && pathObj is List<object?> pl)
                    {
                        foreach (var p in pl) if (p is byte[] pb) pathParts.Add(Encoding.UTF8.GetString(pb));
                    }
                    var rel = string.Join("/", pathParts);
                    files.Add(new TorrentFileInfo { Path = rel, Length = length });
                    totalSize += length;
                }
            }
            else
            {
                long length = AsLong(info, "length");
                files.Add(new TorrentFileInfo { Path = name, Length = length });
                totalSize = length;
            }

            var meta = new TorrentMetadata
            {
                Name = name,
                PieceLength = pieceLength,
                PieceCount = pieceCount,
                TotalSize = totalSize,
                Files = files,
                IsMultiFile = isMulti,
                Announce = AsString(dict, "announce"),
                AnnounceList = ParseAnnounceList(dict),
                InfoHash = ComputeInfoHash(infoObj),
                CreatedBy = AsString(dict, "created by"),
                CreationDate = AsLong(dict, "creation date")
            };
            return meta;
        }

        private static List<string>? ParseAnnounceList(Dictionary<string, object?> dict)
        {
            if (!dict.TryGetValue("announce-list", out var alObj)) return null;
            if (alObj is not List<object?> al) return null;
            var list = new List<string>();
            foreach (var tier in al)
            {
                if (tier is List<object?> tl)
                {
                    foreach (var tr in tl)
                    {
                        if (tr is byte[] tb) list.Add(Encoding.UTF8.GetString(tb));
                    }
                }
            }
            return list.Count == 0 ? null : list;
        }

        /// <summary>
        /// 计算 info 字典的 SHA1，作为种子 ID。
        /// </summary>
        public static string ComputeInfoHash(object? infoObj)
        {
            var encoded = EncodeBencode(infoObj);
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(encoded);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 把 bencode 对象重新编码为字节流。
        /// </summary>
        public static byte[] EncodeBencode(object? obj)
        {
            using var ms = new MemoryStream();
            EncodeInto(ms, obj);
            return ms.ToArray();
        }

        private static void EncodeInto(Stream s, object? obj)
        {
            switch (obj)
            {
                case null:
                    throw new ArgumentNullException(nameof(obj));
                case long l:
                    WriteAscii(s, "i");
                    WriteAscii(s, l.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteAscii(s, "e");
                    break;
                case int i:
                    WriteAscii(s, "i");
                    WriteAscii(s, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteAscii(s, "e");
                    break;
                case byte[] b:
                    WriteAscii(s, b.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteAscii(s, ":");
                    s.Write(b, 0, b.Length);
                    break;
                case string str:
                    var bytes = Encoding.UTF8.GetBytes(str);
                    WriteAscii(s, bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteAscii(s, ":");
                    s.Write(bytes, 0, bytes.Length);
                    break;
                case List<object?> list:
                    WriteAscii(s, "l");
                    foreach (var item in list) EncodeInto(s, item);
                    WriteAscii(s, "e");
                    break;
                case Dictionary<string, object?> dict:
                    WriteAscii(s, "d");
                    // bencode 字典键必须按字典序排序
                    var keys = new List<string>(dict.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var k in keys)
                    {
                        var kb = Encoding.UTF8.GetBytes(k);
                        WriteAscii(s, kb.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        WriteAscii(s, ":");
                        s.Write(kb, 0, kb.Length);
                        EncodeInto(s, dict[k]);
                    }
                    WriteAscii(s, "e");
                    break;
                default:
                    throw new FormatException($"不支持的 bencode 类型: {obj?.GetType()}");
            }
        }

        private static void WriteAscii(Stream s, string text)
        {
            var b = Encoding.ASCII.GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        public static string? AsString(Dictionary<string, object?> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return null;
            return v switch
            {
                string s => s,
                byte[] b => Encoding.UTF8.GetString(b),
                _ => v.ToString()
            };
        }

        public static byte[]? AsBytes(Dictionary<string, object?> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return null;
            return v as byte[];
        }

        public static long AsLong(Dictionary<string, object?> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return 0;
            return v switch
            {
                long l => l,
                int i => i,
                _ => 0
            };
        }
    }

    public class TorrentMetadata
    {
        public string Name { get; set; } = "";
        public long TotalSize { get; set; }
        public long PieceLength { get; set; }
        public int PieceCount { get; set; }
        public bool IsMultiFile { get; set; }
        public List<TorrentFileInfo> Files { get; set; } = new();
        public string? Announce { get; set; }
        public List<string>? AnnounceList { get; set; }
        public string InfoHash { get; set; } = "";
        public string? CreatedBy { get; set; }
        public long CreationDate { get; set; }
    }

    public class TorrentFileInfo
    {
        public string Path { get; set; } = "";
        public long Length { get; set; }
    }
}
