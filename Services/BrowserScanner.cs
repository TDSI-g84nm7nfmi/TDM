using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TDM.Services
{
    /// <summary>
    /// 扫描系统中安装的 Chromium 内核浏览器（含 Tabbit、Chrome、Edge、Brave、Opera、Vivaldi、QQ 浏览器、360、Cent、Yandex 等）。
    /// 并提供检查 native host 是否已注册、安装、卸载的功能。
    /// </summary>
    public static class BrowserScanner
    {
        public const string NativeHostName = "com.tdm.app";

        public class BrowserInfo
        {
            public string Id { get; set; } = "";          // 内部 ID（用于 native host 注册路径）
            public string Name { get; set; } = "";         // 显示名
            public string Vendor { get; set; } = "";       // 厂商
            public string? ExecutablePath { get; set; }    // 主程序路径
            public string RegistryKey { get; set; } = "";  // 在 HKLM/HKCU Software 下查安装的注册表 key（相对于 ...\Clients\ 或 \Capabilities\）
            public List<string> NativeHostRegistryPaths { get; set; } = new(); // 已知的 NativeMessagingHosts 路径
            public bool IsChromium { get; set; } = true;
            public string? IconPath { get; set; }

            public bool NativeHostRegistered
            {
                get
                {
                    foreach (var path in NativeHostRegistryPaths)
                    {
                        string sub;
                        RegistryKey? hive;
                        if (path.StartsWith("HKLM:", StringComparison.OrdinalIgnoreCase))
                        {
                            sub = path.Substring(5);
                            hive = Registry.LocalMachine;
                        }
                        else if (path.StartsWith("HKCU:", StringComparison.OrdinalIgnoreCase))
                        {
                            sub = path.Substring(5);
                            hive = Registry.CurrentUser;
                        }
                        else
                        {
                            // 默认按 HKCU（当前用户）查找，这是无管理员权限安装时唯一能写的位置
                            sub = path;
                            hive = Registry.CurrentUser;
                        }
                        try
                        {
                            using var k = hive?.OpenSubKey(sub);
                            if (k != null) return true;
                        }
                        catch { }
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// 扫描所有可能安装的浏览器，并返回已安装的列表。
        /// </summary>
        public static List<BrowserInfo> Scan()
        {
            var result = new List<BrowserInfo>();

            // Chrome
            var chrome = TryFromRegistry(
                id: "chrome",
                name: "Google Chrome",
                vendor: "Google",
                keys: new[] {
                    @"SOFTWARE\Google\Chrome\BLBeacon",
                    @"SOFTWARE\WOW6432Node\Google\Chrome\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\Google\Chrome\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Google\Chrome\Application\chrome.exe");
            if (chrome != null) result.Add(chrome);

            // Edge (Chromium)
            var edge = TryFromRegistry(
                id: "edge",
                name: "Microsoft Edge",
                vendor: "Microsoft",
                keys: new[] {
                    @"SOFTWARE\Microsoft\Edge\BLBeacon",
                    @"SOFTWARE\WOW6432Node\Microsoft\Edge\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\Microsoft\Edge\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Microsoft\Edge\Application\msedge.exe");
            if (edge != null) result.Add(edge);

            // Tabbit (国产 Chromium 浏览器)
            var tabbit = TryFromRegistry(
                id: "tabbit",
                name: "Tabbit 浏览器",
                vendor: "Tabbit",
                keys: new[] {
                    @"SOFTWARE\Tabbit\TabbitBrowser",
                    @"SOFTWARE\WOW6432Node\Tabbit\TabbitBrowser"
                },
                nativeHosts: new[] {
                    @"Software\Tabbit\TabbitBrowser\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Tabbit\TabbitBrowser\Application\tabbit.exe");
            if (tabbit != null) result.Add(tabbit);

            // Brave
            var brave = TryFromRegistry(
                id: "brave",
                name: "Brave",
                vendor: "Brave Software",
                keys: new[] {
                    @"SOFTWARE\BraveSoftware\Brave-Browser\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"BraveSoftware\Brave-Browser\Application\brave.exe");
            if (brave != null) result.Add(brave);

            // Vivaldi
            var vivaldi = TryFromRegistry(
                id: "vivaldi",
                name: "Vivaldi",
                vendor: "Vivaldi Technologies",
                keys: new[] {
                    @"SOFTWARE\Vivaldi\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\Vivaldi\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Vivaldi\Application\vivaldi.exe");
            if (vivaldi != null) result.Add(vivaldi);

            // Opera / OperaGX
            var opera = TryFromRegistry(
                id: "opera",
                name: "Opera",
                vendor: "Opera",
                keys: new[] {
                    @"SOFTWARE\OperaSoftware\Opera\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\OperaSoftware\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Programs\Opera\opera.exe");
            if (opera != null) result.Add(opera);

            // Yandex
            var yandex = TryFromRegistry(
                id: "yandex",
                name: "Yandex Browser",
                vendor: "Yandex",
                keys: new[] {
                    @"SOFTWARE\Yandex\YandexBrowser\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\Yandex\YandexBrowser\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Yandex\YandexBrowser\Application\browser.exe");
            if (yandex != null) result.Add(yandex);

            // 360 极速浏览器
            var s360 = TryFromRegistry(
                id: "360chrome",
                name: "360 极速浏览器",
                vendor: "360",
                keys: new[] {
                    @"SOFTWARE\360Chrome\Chrome\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\360Chrome\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"360Chrome\Chrome\Application\360chrome.exe");
            if (s360 != null) result.Add(s360);

            // QQ 浏览器
            var qq = TryFromRegistry(
                id: "qqbrowser",
                name: "QQ 浏览器",
                vendor: "Tencent",
                keys: new[] {
                    @"SOFTWARE\Tencent\QQBrowser\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\Tencent\QQBrowser\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"Tencent\QQBrowser\QQBrowser.exe");
            if (qq != null) result.Add(qq);

            // CentBrowser
            var cent = TryFromRegistry(
                id: "centbrowser",
                name: "Cent Browser",
                vendor: "Cent Studio",
                keys: new[] {
                    @"SOFTWARE\CentStudio\CentBrowser\BLBeacon"
                },
                nativeHosts: new[] {
                    @"Software\CentStudio\NativeMessagingHosts\" + NativeHostName
                },
                exeRelative: @"CentBrowser\Application\chrome.exe");
            if (cent != null) result.Add(cent);

            // 通过 Capabilities\StartMenuInternet（更全的"已注册为系统默认浏览器"列表）补充
            result = MergeFromCapabilities(result);

            return result;
        }

        private static BrowserInfo? TryFromRegistry(string id, string name, string vendor, string[] keys, string[] nativeHosts, string exeRelative)
        {
            string? installDir = null;

            // 1) 直接从已知注册表 key 读 install_dir / Uninstall
            foreach (var keyPath in keys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath)
                                    ?? Registry.CurrentUser.OpenSubKey(keyPath);
                    if (key != null)
                    {
                        // BLBeacon 不含路径；尝试 CommonFiles 或其他位置
                        var path = key.GetValue("install_dir") as string
                                   ?? key.GetValue("Path") as string;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            installDir = path;
                            break;
                        }
                    }
                }
                catch { }
            }

            // 2) 从 Uninstall 读 InstallLocation
            if (installDir == null)
            {
                installDir = TryFromUninstall(name, vendor);
            }

            // 3) 拼默认 Program Files 路径
            if (installDir == null)
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var candidates = new[] { Path.Combine(pf, exeRelative), Path.Combine(pf86, exeRelative) };
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        installDir = Path.GetDirectoryName(c);
                        break;
                    }
                }
            }

            // 4) LocalAppData
            if (installDir == null)
            {
                var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var c1 = Path.Combine(lad, exeRelative);
                if (File.Exists(c1)) installDir = Path.GetDirectoryName(c1);
            }

            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
                return null;

            // 找主程序
            var exeName = Path.GetFileName(exeRelative);
            var exePath = Path.Combine(installDir, exeName);
            if (!File.Exists(exePath))
            {
                // 退而求其次，找目录里第一个 .exe
                var exes = Directory.GetFiles(installDir, "*.exe");
                if (exes.Length > 0) exePath = exes[0];
                else return null;
            }

            return new BrowserInfo
            {
                Id = id,
                Name = name,
                Vendor = vendor,
                ExecutablePath = exePath,
                RegistryKey = keys.FirstOrDefault() ?? "",
                NativeHostRegistryPaths = nativeHosts.ToList()
            };
        }

        private static string? TryFromUninstall(string displayHint, string vendorHint)
        {
            try
            {
                var paths = new[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                foreach (var p in paths)
                {
                    using var root = Registry.LocalMachine.OpenSubKey(p);
                    if (root == null) continue;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(sub);
                        if (k == null) continue;
                        var name = k.GetValue("DisplayName") as string ?? "";
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.IndexOf(displayHint, StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf(vendorHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var loc = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) return loc;
                    }
                }
            }
            catch { }
            return null;
        }

        private static List<BrowserInfo> MergeFromCapabilities(List<BrowserInfo> existing)
        {
            try
            {
                var capRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                if (capRoot == null) return existing;
                foreach (var clientName in capRoot.GetSubKeyNames())
                {
                    if (existing.Any(b => string.Equals(b.Id, clientName, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    // 只关心 Chromium 内核
                    if (!IsChromiumClient(clientName)) continue;

                    using var clientKey = capRoot.OpenSubKey(clientName);
                    if (clientKey == null) continue;
                    var displayName = clientKey.GetValue("") as string ?? clientName;

                    string? exe = null;
                    using (var cmdKey = clientKey.OpenSubKey(@"shell\open\command"))
                    {
                        if (cmdKey != null)
                        {
                            var cmd = cmdKey.GetValue("") as string ?? "";
                            exe = ExtractExePath(cmd);
                        }
                    }
                    if (exe == null || !File.Exists(exe)) continue;

                    existing.Add(new BrowserInfo
                    {
                        Id = clientName.ToLowerInvariant(),
                        Name = displayName,
                        Vendor = "",
                        ExecutablePath = exe,
                        RegistryKey = @"Clients\StartMenuInternet\" + clientName,
                        NativeHostRegistryPaths = new List<string>
                        {
                            @"Software\" + clientName + @"\NativeMessagingHosts\" + NativeHostName
                        }
                    });
                }
            }
            catch { }
            return existing;
        }

        private static bool IsChromiumClient(string client)
        {
            var chromium = new[] { "chrome", "edge", "brave", "vivaldi", "opera", "yandex",
                "tabbit", "360", "qq", "cent", "sogou", "maxthon", "coccoc", "iridium" };
            var c = client.ToLowerInvariant();
            return chromium.Any(k => c.Contains(k));
        }

        private static string? ExtractExePath(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) return cmd.Split('"')[1];
            return cmd.Split(' ')[0];
        }

        /// <summary>
        /// 为某个浏览器注册 native host（写入注册表，指向 com.tdm.app.json）。
        /// 使用 HKCU，无需管理员权限。
        /// </summary>
        public static bool RegisterNativeHost(BrowserInfo browser, string manifestPath, string batPath)
        {
            try
            {
                if (!File.Exists(manifestPath)) return false;
                if (!File.Exists(batPath)) return false;

                bool ok = true;
                foreach (var path in browser.NativeHostRegistryPaths)
                {
                    // 去掉 hive 前缀（HKCU: / HKLM:）以得到子键路径
                    string sub = path;
                    if (sub.StartsWith("HKCU:", StringComparison.OrdinalIgnoreCase)) sub = sub.Substring(5);
                    else if (sub.StartsWith("HKLM:", StringComparison.OrdinalIgnoreCase)) sub = sub.Substring(5);
                    // HKLM 通常需要管理员权限；优先尝试 HKCU
                    try
                    {
                        using var k = Registry.CurrentUser.CreateSubKey(sub, writable: true);
                        if (k != null) k.SetValue("", manifestPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"注册 native host 失败 {sub}: {ex.Message}");
                        ok = false;
                    }
                }
                if (ok) Logger.Info($"已为 {browser.Name} 注册 native host");
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Warn("RegisterNativeHost 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 卸载 native host 注册。
        /// </summary>
        public static void UnregisterAll()
        {
            foreach (var b in Scan())
            {
                foreach (var path in b.NativeHostRegistryPaths)
                {
                    if (path.StartsWith("HKCU:", StringComparison.OrdinalIgnoreCase))
                    {
                        var sub = path.Substring(5);
                        try { Registry.CurrentUser.DeleteSubKeyTree(sub, throwOnMissingSubKey: false); } catch { }
                    }
                }
            }
        }
    }
}
