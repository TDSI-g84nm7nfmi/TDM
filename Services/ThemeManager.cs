using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace TDM.Services
{
    public static class ThemeManager
    {
        public const string Purple = "purple";
        public const string Blue = "blue";
        public const string Green = "green";
        public const string Dark = "dark";
        public const string Pink = "pink";

        public static string Current { get; private set; } = "purple";

        public static event EventHandler? ThemeChanged;

        private static readonly Dictionary<string, string[]> ThemePalettes = new()
        {
            [Blue] = new[] {
                "#4a9eff","#2d7be0","#80bdff","#f0f5ff","#f5f9ff","#1a1a2e","#666680","#d0d8e8",
                "#e8f0ff","#334a9eff","#204a9eff","#FFFFFF","#F5F5F5"
            },
            [Green] = new[] {
                "#4caf50","#388e3c","#81c784","#f4faf4","#ffffff","#1f1f1f","#666666","#e0e0e0",
                "#e8f5e9","#334caf50","#204caf50","#FFFFFF","#F5F5F5"
            },
            [Dark] = new[] {
                "#7c4dff","#5e35b1","#b388ff","#252535","#1e1e2e","#eeeeee","#a0a0a0","#333344",
                "#33b388ff","#337c4dff","#207c4dff","#2a2a3a","#333344"
            },
            [Pink] = new[] {
                "#ff5e9c","#d43681","#ffa1c5","#fff5fa","#ffffff","#1f1f1f","#666666","#e0e0e0",
                "#ffe4f0","#33ff5e9c","#20ff5e9c","#FFFFFF","#F5F5F5"
            },
            [Purple] = new[] {
                "#a259e6","#7c3fc4","#c98aff","#faf7ff","#ffffff","#1f1f1f","#666666","#e0e0e0",
                "#f3eaff","#33a259e6","#20a259e6","#FFFFFF","#F5F5F5"
            }
        };

        public static void Load()
        {
            var t = SettingsService.Current.Theme ?? Purple;
            Apply(t);
        }

        public static void Set(string theme)
        {
            Apply(theme);
            SettingsService.Update(s => s.Theme = theme);
        }

        public static void CycleTheme()
        {
            var order = new[] { Blue, Green, Purple, Pink, Dark };
            int idx = Array.IndexOf(order, Current);
            if (idx < 0) idx = 0;
            Set(order[(idx + 1) % order.Length]);
        }

        public static void PersistCurrent()
        {
            SettingsService.Update(s => s.Theme = Current);
            SettingsService.Save();
        }

        public static void Apply(string theme)
        {
            if (!ThemePalettes.TryGetValue(theme, out var hexes))
            {
                theme = Purple;
                hexes = ThemePalettes[Purple];
            }
            Current = theme;

            var app = Application.Current;
            if (app == null) return;

            var resources = app.Resources;
            var target = BuildColorMap(hexes);

            foreach (var kv in target)
                resources[kv.Key] = kv.Value;

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static Dictionary<string, Color> BuildColorMap(string[] hexes)
        {
            var map = new Dictionary<string, Color>(StringComparer.Ordinal);
            string[] keys = {
                "PrimaryColor","PrimaryDarkColor","AccentColor",
                "BackgroundTopColor","BackgroundBottomColor",
                "TextColor","SubTextColor","BorderColor",
                "HoverColor","PressedColor","SelectedRowColor",
                "CardBackgroundColor","AltRowBackgroundColor"
            };
            for (int i = 0; i < keys.Length; i++)
                map[keys[i]] = ParseColor(hexes[i]);
            return map;
        }

        private static Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
            return Color.FromArgb(255, 128, 0, 128);
        }
    }
}
