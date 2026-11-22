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
                // Dark Canvas & Atmospheric Gradient
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1D));
                
                var darkAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.38, 0),
                    EndPoint = new Point(0.62, 1)
                };
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x2A, 0x26, 0x49), 0.0));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x1F, 0x1E, 0x31), 0.25));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x18, 0x18, 0x24), 0.60));
                darkAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x16, 0x16, 0x1D), 1.0));
                res["AtmosphericGradient"] = darkAtmosphere;

                // Dark Translucent Panels (Level 1 Frosted Glass)
                res["SidebarGlassBrush"] = new SolidColorBrush(Color.FromArgb(0x70, 0x25, 0x25, 0x37));
                res["MainAreaGlassBrush"] = new SolidColorBrush(Color.FromArgb(0x58, 0x25, 0x25, 0x37));

                // Opaque Dark Cards & Dialog Surfaces (Never see-through)
                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x37));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromRgb(0x39, 0x3A, 0x56));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0x25, 0x25, 0x37));

                // Dark Borders
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x38, 0x6C, 0x6D, 0x93));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0x6C, 0x6D, 0x93));
                res["BorderDarkBrush"] = new SolidColorBrush(Color.FromArgb(0x38, 0x6C, 0x6D, 0x93));

                // High Contrast Dark Typography
                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x9F, 0xA0, 0xC3));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0x6D, 0x93));

                // Dark Mode Accents & Status
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
                res["AccentSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xDD, 0x52));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                res["StatusWarningBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xDD, 0x52));
                res["StatusDangerBrush"] = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

                // Progress Bar Track
                res["ProgressBarTrackBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x24, 0x38));

                // ScrollBar & Aliases
                res["ScrollBarThumbBrush"] = new SolidColorBrush(Color.FromArgb(0x50, 0x6C, 0x6D, 0x93));
                res["ScrollBarThumbHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x90, 0x6C, 0x6D, 0x93));
                res["ScrollBarThumbDragBrush"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                res["BgCardBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x37));
            }
            else
            {
                // Light Canvas & Clean Fluid Gradient
                res["CanvasBgBrush"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF8));

                var lightAtmosphere = new LinearGradientBrush
                {
                    StartPoint = new Point(0.38, 0),
                    EndPoint = new Point(0.62, 1)
                };
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xE0, 0xE7, 0xFF), 0.0));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xEB, 0xF0, 0xFA), 0.35));
                lightAtmosphere.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xF4, 0xF7, 0xFC), 1.0));
                res["AtmosphericGradient"] = lightAtmosphere;

                // Light Translucent Frosted Glass Panels (Clean pearl aesthetics)
                res["SidebarGlassBrush"] = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
                res["MainAreaGlassBrush"] = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

                // Opaque Solid Clean White Surfaces (Zero bleed-through on dialogs and cards)
                res["SurfaceDarkBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                res["SurfaceRaisedBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                res["SurfaceSubtleBrush"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6));

                // Light Mode Crisp Borders
                res["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0xC0, 0xCB, 0xD5, 0xE1));
                res["BorderHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                res["BorderDarkBrush"] = new SolidColorBrush(Color.FromArgb(0xC0, 0xCB, 0xD5, 0xE1));

                // High Contrast Light Typography (Deep slate black for effortless readability)
                res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

                // Light Mode Accents (Preserve Indigo brand; Amber for readable warnings)
                res["AccentPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                res["AccentGlowBrush"] = new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5));
                res["AccentSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                res["StatusSafeBrush"] = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                res["StatusWarningBrush"] = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                res["StatusDangerBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

                // Light Mode Soft Slate Progress Bar Track
                res["ProgressBarTrackBrush"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

                // ScrollBar & Aliases
                res["ScrollBarThumbBrush"] = new SolidColorBrush(Color.FromArgb(0x60, 0x94, 0xA3, 0xB8));
                res["ScrollBarThumbHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                res["ScrollBarThumbDragBrush"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                res["BgCardBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            }

            ThemeChanged?.Invoke(darkMode);
        }
    }
}
