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
                // Proton-Style High Dynamic Range Aurora Spotlight into Deep Obsidian Void
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x10));
                
                var darkAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.38, 0),
                    EndPoint = new Point(0.62, 1)
                };
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x2C, 0x12, 0x50), 0.0));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x12, 0x2A, 0x50), 0.20));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x0B, 0x15, 0x2A), 0.52));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x06, 0x09, 0x10), 1.0));
                res["AtmosphericGradient"] = darkAtmosphere;

                // Deep Obsidian Slate Cards with Crisp High Contrast
                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xEE, 0x0C, 0x11, 0x1C));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xF4, 0x12, 0x19, 0x29));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xC0, 0x0E, 0x14, 0x22));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x30, 0x45, 0x60, 0xA0));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x70, 0x5A, 0x7C, 0xE0));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFD));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xC0));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0x80));

                // Vortex Logo Colors: Royal Violet Shield & Electric Azure Swirl
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xA3, 0xFF));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
            }
            else
            {
                // Vortex Light: Soft Arctic Azure & Lavender Mist + Translucent Pearl
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFB));

                var lightAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xE2, 0xEB, 0xFE), 0.0));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xEC, 0xE7, 0xFE), 0.40));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xF5, 0xF7, 0xFD), 1.0));
                res["AtmosphericGradient"] = lightAtmosphere;

                // Translucent floating pearl cards
                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0x40, 0xF0, 0xF4, 0xFC));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0xD0, 0xDA, 0xF5));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xB0, 0xB0, 0xC4, 0xF5));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x0C, 0x13, 0x26));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x4A, 0x6B));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x7E, 0x9E));

                // Vivid Electric Blue & Deep Purple in Light Mode
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x5E, 0x3D, 0xFA));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x77, 0xEE));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
            }

            ThemeChanged?.Invoke(darkMode);
        }
    }
}
