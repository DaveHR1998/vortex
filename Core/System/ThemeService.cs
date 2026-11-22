using System;
using System.Windows;
using System.Windows.Media;

namespace Vortex.Core.SystemIntegration
{
    public class ThemeService
    {
        public bool IsDarkMode { get; private set; } = true;

        public event Action<bool>? ThemeChanged;

        public void ToggleTheme()
        {
            SetTheme(!IsDarkMode);
        }

        public void SetTheme(bool darkMode)
        {
            IsDarkMode = darkMode;
            var res = Application.Current.Resources;

            if (darkMode)
            {
                // Proton-Inspired Dark Emerald-Teal Aurora + Obsidian Base
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0x07, 0x0E, 0x11));
                
                var darkAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x0B, 0x30, 0x29), 0.0));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x09, 0x24, 0x20), 0.30));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x08, 0x16, 0x19), 0.70));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x06, 0x0C, 0x0E), 1.0));
                res["AtmosphericGradient"] = darkAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xEE, 0x0E, 0x18, 0x1C));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x23, 0x28));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x0F, 0x1C, 0x20));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x3D, 0x29, 0x4E, 0x48));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x85, 0x3D, 0x74, 0x6B));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xFA, 0xF8));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x8F, 0xA8, 0xA4));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x51, 0x70, 0x6B));

                // Proton Signature Electric Violet & Mint
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
            }
            else
            {
                // Proton-Inspired Luminous Mint Mist & Pearl Light Mode
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xF7, 0xF4));

                var lightAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xA5, 0xEF, 0xE0), 0.0));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xD8, 0xF5, 0xED), 0.40));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xEE, 0xF9, 0xF6), 1.0));
                res["AtmosphericGradient"] = lightAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xE4, 0xF6, 0xF2, 0xF8));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xCD, 0xEC, 0xE4));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x9F, 0xE2, 0xD3));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x09, 0x26, 0x22));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x5E, 0x58));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0x94, 0x8D));

                // Proton Violet & Emerald in Light Mode
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xB7, 0x7D));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xA8, 0x6B));
            }

            ThemeChanged?.Invoke(darkMode);
        }
    }
}
