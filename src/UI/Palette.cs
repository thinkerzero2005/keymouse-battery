using System.Windows.Media;

namespace KeyMouseBattery.UI
{
    /// <summary>界面配色（统一在此维护，保持清爽的浅色风格）。</summary>
    internal static class Palette
    {
        public static readonly SolidColorBrush TextPrimary = Make(0x11, 0x18, 0x27);
        public static readonly SolidColorBrush TextSecondary = Make(0x64, 0x74, 0x8B);
        public static readonly SolidColorBrush TextTertiary = Make(0x94, 0xA3, 0xB8);
        public static readonly SolidColorBrush Border = Make(0xE8, 0xED, 0xF3);
        public static readonly SolidColorBrush Card = Make(0xF8, 0xFA, 0xFC);
        public static readonly SolidColorBrush Track = Make(0xED, 0xF1, 0xF6);
        public static readonly SolidColorBrush Accent = Make(0x2F, 0x6B, 0xED);
        public static readonly SolidColorBrush AccentSoft = Make(0xEF, 0xF5, 0xFF);
        public static readonly SolidColorBrush Green = Make(0x16, 0xA3, 0x4A);
        public static readonly SolidColorBrush GreenSoft = Make(0xEC, 0xFD, 0xF5);
        public static readonly SolidColorBrush Amber = Make(0xF5, 0x9E, 0x0B);
        public static readonly SolidColorBrush Red = Make(0xDC, 0x26, 0x26);
        public static readonly SolidColorBrush Gray = Make(0x9C, 0xA3, 0xAF);
        public static readonly SolidColorBrush Chip = Make(0xEE, 0xF2, 0xF7);
        public static readonly SolidColorBrush White = Make(0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Dark = Make(0x1F, 0x29, 0x37);
        public static readonly SolidColorBrush Hover = Make(0xF1, 0xF5, 0xF9);

        private static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static Brush ForPercent(int percent, int threshold)
        {
            if (percent < threshold) return Red;
            if (percent < threshold + 20) return Amber;
            return Green;
        }
    }
}
