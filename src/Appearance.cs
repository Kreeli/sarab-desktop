using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SarabDesktop
{
    public static class Appearance
    {
        static readonly Dictionary<string, string> accents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mint"] = "#68D5AB", ["ocean"] = "#72BFE6", ["violet"] = "#B5A0F4", ["amber"] = "#F0BB62"
        };

        public static void Apply(Window window, AppSettings settings)
        {
            bool light = settings.ThemeMode == "light" || settings.ThemeMode == "system" && IsSystemLight();
            string accent = accents.ContainsKey(settings.AccentColor ?? "") ? accents[settings.AccentColor] : accents["mint"];
            var colors = new Dictionary<string, string>
            {
                ["AppBg"] = light ? "#F2F6F4" : "#151D22",
                ["NavBg"] = light ? "#E5EBF0EC" : "#D90F171B",
                ["CardSurface"] = light ? "#F8FFFFFF" : "#ED1D272D",
                ["SurfaceAlt"] = light ? "#EAF5F8F4" : "#DC2A383F",
                ["Elevated"] = light ? "#FAFFFFFF" : "#F3243238",
                ["Border"] = light ? "#D6E1DB" : "#39474E",
                ["Text"] = light ? "#17251F" : "#EAF1ED",
                ["MutedText"] = light ? "#5D7067" : "#A6B4AE",
                ["SoftText"] = light ? "#42574E" : "#C6D2CC",
                ["CardEdge"] = light ? "#DCE7E1" : "#27FFFFFF",
                ["NavHoverBorder"] = Blend(accent, light ? "#D0DED6" : "#46565D", 0.36),
                ["NavActive"] = Blend(accent, light ? "#FFFFFF" : "#1D272D", light ? 0.13 : 0.28),
                ["Input"] = light ? "#FFFFFF" : "#141E23",
                ["InputBorder"] = light ? "#C4D1CA" : "#46565D",
                ["Accent"] = accent,
                ["OnAccent"] = "#10221A",
                ["AccentSoft"] = Blend(accent, light ? "#FFFFFF" : "#151D22", light ? 0.16 : 0.22),
                ["Selection"] = Blend(accent, light ? "#FFFFFF" : "#151D22", light ? 0.19 : 0.28),
                ["ToggleOff"] = light ? "#B5C2BB" : "#526168",
                ["ToggleThumb"] = light ? "#FFFFFF" : "#F1F5F2",
                ["Danger"] = light ? "#A93445" : "#F0A5AE",
                ["DangerSoft"] = light ? "#FBEAEC" : "#472B32",
                ["Success"] = light ? "#187653" : "#86D9B0",
                ["Warning"] = light ? "#8B631E" : "#EBC879",
                ["Info"] = light ? "#3D6F86" : "#9AC9DC"
            };
            if (SystemParameters.HighContrast)
            {
                string background = SystemColors.WindowColor.ToString();
                string foreground = SystemColors.WindowTextColor.ToString();
                string highlight = SystemColors.HighlightColor.ToString();
                string highlightText = SystemColors.HighlightTextColor.ToString();
                string control = SystemColors.ControlColor.ToString();
                colors["AppBg"] = background;
                colors["NavBg"] = background;
                colors["CardSurface"] = background;
                colors["SurfaceAlt"] = control;
                colors["Elevated"] = background;
                colors["Border"] = foreground;
                colors["Text"] = foreground;
                colors["MutedText"] = foreground;
                colors["SoftText"] = foreground;
                colors["CardEdge"] = foreground;
                colors["NavHoverBorder"] = highlight;
                colors["NavActive"] = highlight;
                colors["Input"] = background;
                colors["InputBorder"] = foreground;
                colors["Accent"] = highlight;
                colors["OnAccent"] = highlightText;
                colors["AccentSoft"] = background;
                colors["Selection"] = highlight;
                colors["ToggleOff"] = foreground;
                colors["ToggleThumb"] = highlightText;
                colors["Danger"] = foreground;
                colors["DangerSoft"] = background;
                colors["Success"] = foreground;
                colors["Warning"] = foreground;
                colors["Info"] = foreground;
            }
            foreach (var pair in colors) window.Resources[pair.Key] = MakeBrush(pair.Value);
            window.Background = (Brush)window.Resources["AppBg"];
            window.Foreground = (Brush)window.Resources["Text"];
        }

        public static bool IsSystemLight()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return Convert.ToInt32(value) != 0;
            }
            catch { return true; }
        }

        public static Brush Brush(Window window, string key) => (Brush)window.Resources[key];
        static SolidColorBrush MakeBrush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        static string Blend(string foreground, string background, double amount)
        {
            Color a = (Color)ColorConverter.ConvertFromString(foreground), b = (Color)ColorConverter.ConvertFromString(background);
            return string.Format("#{0:X2}{1:X2}{2:X2}", (int)(a.R * amount + b.R * (1 - amount)), (int)(a.G * amount + b.G * (1 - amount)), (int)(a.B * amount + b.B * (1 - amount)));
        }
    }
}
