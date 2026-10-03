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
                // Proton-Inspired Matte Charcoal + Ambient Top Crimson Glow
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C));
                
                var darkAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x3D, 0x1B, 0x2A), 0.0));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x24, 0x1A, 0x24), 0.35));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x18, 0x18, 0x1C), 0.85));
                res["AtmosphericGradient"] = darkAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xEA, 0x20, 0x1A, 0x26));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xF0, 0x2A, 0x22, 0x33));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xB0, 0x24, 0x1D, 0x2D));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x60, 0x55, 0x46, 0x66));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xA0, 0x7E, 0x66, 0x99));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFD));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0x9E, 0xB0));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x71, 0x69, 0x7C));

                // Proton Violet & Turquoise
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
            }
            else
            {
                // Clean Modern Light Mode
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF8));

                var lightAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1)
                };
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xEB, 0xE7, 0xF8), 0.0));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF8), 0.6));
                res["AtmosphericGradient"] = lightAtmosphere;

                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0xE0, 0xF9, 0xFA, 0xFC));
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xE2, 0xE8, 0xF0));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xCB, 0xD5, 0xE1));

                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

                // Vibrant Electric Indigo in Light Mode
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x5E, 0x3D, 0xFA));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }

            ThemeChanged?.Invoke(darkMode);
        }
    }
}
