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
                // Vortex Signature: Cosmic Violet & Electric Azure Aurora + Deep Obsidian Navy Base
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0x07, 0x0A, 0x12));
                
                var darkAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x1C, 0x13, 0x35), 0.0));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x10, 0x1C, 0x38), 0.30));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x0A, 0x0F, 0x1F), 0.70));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x07, 0x0A, 0x12), 1.0));
                res["AtmosphericGradient"] = darkAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x14, 0x24));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x1C, 0x30));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x11, 0x16, 0x28));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x45, 0x2C, 0x38, 0x60));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x85, 0x45, 0x5C, 0xA0));

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
                // Vortex Light: Soft Arctic Azure & Lavender Mist + Frosted Pearl
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFC));

                var lightAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xE6, 0xED, 0xFE), 0.0));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xEC, 0xE7, 0xFD), 0.40));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xF8, 0xFA, 0xFD), 1.0));
                res["AtmosphericGradient"] = lightAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xEB, 0xF0, 0xFA, 0xFF));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xD2, 0xDC, 0xF5));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xAA, 0xBE, 0xF0));

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
