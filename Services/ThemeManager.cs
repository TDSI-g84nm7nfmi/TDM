using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TDM.Services;

namespace TDM.Services
{
    /// <summary>
    /// 主题管理器。负责切换主题色彩并驱动 WPF 平滑过渡动画（Windows 风格的高雅丝滑过渡）。
    /// </summary>
    public static class ThemeManager
    {
        public const string Purple = "purple";
        public const string Blue = "blue";
        public const string Green = "green";
        public const string Dark = "dark";
        public const string Pink = "pink";

        public static string Current { get; private set; } = "purple";

        public static event EventHandler? ThemeChanged;

        // 主题调色板：每个主题一组 hex，按 ColorKeys 顺序
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

        // 笔刷资源 key（用于直接动画 brush.Color）
        private static readonly (string BrushKey, string ColorHexIdx)[] BrushEntries = {
            ("PrimaryBrush", "PrimaryColor"),
            ("PrimaryDarkBrush", "PrimaryDarkColor"),
            ("AccentBrush", "AccentColor"),
            ("BackgroundTopBrush", "BackgroundTopColor"),
            ("BackgroundBottomBrush", "BackgroundBottomColor"),
            ("TextBrush", "TextColor"),
            ("SubTextBrush", "SubTextColor"),
            ("BorderBrush", "BorderColor"),
            ("HoverBrush", "HoverColor"),
            ("PressedBrush", "PressedColor"),
            ("SelectedRowBrush", "SelectedRowColor"),
            ("CardBackground", "CardBackgroundColor"),
            ("AltRowBackground", "AltRowBackgroundColor")
        };

        public static void Load()
        {
            var t = SettingsService.Current.Theme ?? Blue;
            Apply(t, animate: false);
        }

        public static void Set(string theme)
        {
            Apply(theme, animate: true);
            SettingsService.Update(s => s.Theme = theme);
        }

        public static void PersistCurrent()
        {
            SettingsService.Update(s => s.Theme = Current);
            SettingsService.Save();
        }

        /// <summary>
        /// 应用主题。如果 animate=true，则用 ColorAnimation 平滑插值，形成 Windows 风格的丝滑过渡。
        /// </summary>
        public static void Apply(string theme, bool animate = true)
        {
            if (!ThemePalettes.TryGetValue(theme, out var hexes)) theme = Purple;
            Current = theme;

            var app = Application.Current;
            if (app == null) return;

            var resources = app.Resources;
            var target = BuildColorMap(hexes);

            if (animate)
            {
                AnimatePalette(resources, target, TimeSpan.FromMilliseconds(420));
            }
            else
            {
                ApplyPaletteImmediate(resources, target);
            }

            UpdateGradientBrushes(resources, target);
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
            {
                map[keys[i]] = (Color)ColorConverter.ConvertFromString(hexes[i]);
            }
            return map;
        }

        private static void ApplyPaletteImmediate(ResourceDictionary resources, Dictionary<string, Color> target)
        {
            foreach (var kv in target) resources[kv.Key] = kv.Value;
            foreach (var (brushKey, colorKey) in BrushEntries)
            {
                if (resources[brushKey] is SolidColorBrush brush)
                {
                    if (brush.IsFrozen) brush = brush.Clone();
                    brush.Color = target[colorKey];
                    resources[brushKey] = brush;
                }
            }
        }

        private static void AnimatePalette(ResourceDictionary resources, Dictionary<string, Color> target, TimeSpan duration)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // 1) 笔刷 Color 属性动画（最直观，UI 立即平滑过渡）
            foreach (var (brushKey, colorKey) in BrushEntries)
            {
                if (resources[brushKey] is SolidColorBrush brush)
                {
                    var startColor = brush.Color;
                    var endColor = target[colorKey];
                    if (startColor == endColor) continue;

                    if (brush.IsFrozen) brush = brush.Clone();
                    resources[brushKey] = brush; // 替换为可写副本

                    var anim = new ColorAnimation(startColor, endColor, duration) { EasingFunction = ease };
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                }
            }

            // 2) 渐变笔刷 GradientStop 动画（必须先 Clone 否则 GradientStop 处于 Frozen 状态无法动画）
            if (resources["PrimaryGradientBrush"] is LinearGradientBrush pgrad)
            {
                if (pgrad.IsFrozen) pgrad = pgrad.Clone();
                resources["PrimaryGradientBrush"] = pgrad;
                AnimateGradientStop(pgrad.GradientStops[0], pgrad.GradientStops[0].Color, target["PrimaryColor"], duration, ease);
                AnimateGradientStop(pgrad.GradientStops[1], pgrad.GradientStops[1].Color, target["AccentColor"], duration, ease);
            }
            if (resources["HeaderGradientBrush"] is LinearGradientBrush hgrad)
            {
                if (hgrad.IsFrozen) hgrad = hgrad.Clone();
                resources["HeaderGradientBrush"] = hgrad;
                AnimateGradientStop(hgrad.GradientStops[0], hgrad.GradientStops[0].Color, target["BackgroundTopColor"], duration, ease);
                AnimateGradientStop(hgrad.GradientStops[1], hgrad.GradientStops[1].Color, target["BackgroundBottomColor"], duration, ease);
            }

            // 3) Color 资源也同步更新（保证后续读取得到新值）
            foreach (var kv in target) resources[kv.Key] = kv.Value;
        }

        private static void AnimateGradientStop(GradientStop stop, Color from, Color to, TimeSpan duration, IEasingFunction ease)
        {
            var anim = new ColorAnimation(from, to, duration) { EasingFunction = ease };
            stop.BeginAnimation(GradientStop.ColorProperty, anim);
        }

        private static void UpdateGradientBrushes(ResourceDictionary resources, Dictionary<string, Color> target)
        {
            if (resources["PrimaryGradientBrush"] is LinearGradientBrush pgrad)
            {
                if (pgrad.IsFrozen) pgrad = pgrad.Clone();
                pgrad.GradientStops[0].Color = target["PrimaryColor"];
                pgrad.GradientStops[1].Color = target["AccentColor"];
                resources["PrimaryGradientBrush"] = pgrad;
            }
            if (resources["HeaderGradientBrush"] is LinearGradientBrush hgrad)
            {
                if (hgrad.IsFrozen) hgrad = hgrad.Clone();
                hgrad.GradientStops[0].Color = target["BackgroundTopColor"];
                hgrad.GradientStops[1].Color = target["BackgroundBottomColor"];
                resources["HeaderGradientBrush"] = hgrad;
            }
        }
    }
}
