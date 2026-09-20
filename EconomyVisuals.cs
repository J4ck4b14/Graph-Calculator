using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace GraphCalculator
{
    internal static class EconomyVisuals
    {
        private static readonly Dictionary<string, Color> CommonResources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gold"] = Color.FromRgb(214, 157, 46),
            ["Gems"] = Color.FromRgb(139, 92, 246),
            ["Gem"] = Color.FromRgb(139, 92, 246),
            ["XP"] = Color.FromRgb(59, 130, 246),
            ["Experience"] = Color.FromRgb(59, 130, 246),
            ["Energy"] = Color.FromRgb(20, 184, 166),
            ["Stamina"] = Color.FromRgb(20, 184, 166),
            ["Tokens"] = Color.FromRgb(234, 88, 12),
            ["Token"] = Color.FromRgb(234, 88, 12),
            ["Wood"] = Color.FromRgb(146, 96, 58),
            ["Iron"] = Color.FromRgb(100, 116, 139),
            ["Ore"] = Color.FromRgb(120, 113, 108),
            ["Food"] = Color.FromRgb(34, 197, 94),
            ["Health"] = Color.FromRgb(220, 38, 38),
            ["Mana"] = Color.FromRgb(14, 116, 144),
            ["Value"] = Color.FromRgb(107, 114, 128),
            ["Resource"] = Color.FromRgb(71, 85, 105)
        };

        private static readonly Color[] FallbackPalette =
        [
            Color.FromRgb(37, 99, 235),
            Color.FromRgb(5, 150, 105),
            Color.FromRgb(217, 119, 6),
            Color.FromRgb(124, 58, 237),
            Color.FromRgb(219, 39, 119),
            Color.FromRgb(8, 145, 178),
            Color.FromRgb(101, 163, 13),
            Color.FromRgb(194, 65, 12),
            Color.FromRgb(79, 70, 229),
            Color.FromRgb(190, 24, 93),
            Color.FromRgb(13, 148, 136),
            Color.FromRgb(147, 51, 234)
        ];

        public static Color ResourceColor(string? resource)
        {
            string key = string.IsNullOrWhiteSpace(resource) ? "Resource" : resource.Trim();
            if (CommonResources.TryGetValue(key, out Color common)) return common;

            uint hash = 2166136261;
            foreach (char c in key.ToUpperInvariant())
            {
                hash ^= c;
                hash *= 16777619;
            }
            return FallbackPalette[(int)(hash % (uint)FallbackPalette.Length)];
        }

        public static SolidColorBrush ResourceBrush(string? resource, byte alpha = 255)
        {
            Color color = ResourceColor(resource);
            color.A = alpha;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static Brush KindBrush(EconomyNodeKind kind) => kind switch
        {
            EconomyNodeKind.Source => Brushes.SeaGreen,
            EconomyNodeKind.Event => Brushes.MediumSeaGreen,
            EconomyNodeKind.Pool => Brushes.RoyalBlue,
            EconomyNodeKind.Gate => Brushes.MediumPurple,
            EconomyNodeKind.Queue => Brushes.SteelBlue,
            EconomyNodeKind.Register => Brushes.DimGray,
            EconomyNodeKind.Converter => Brushes.DarkOrange,
            EconomyNodeKind.Action => Brushes.DarkCyan,
            EconomyNodeKind.Sink => Brushes.Crimson,
            _ => Brushes.SlateGray
        };

        public static string KindGlyph(EconomyNodeKind kind) => kind switch
        {
            EconomyNodeKind.Source => "+",
            EconomyNodeKind.Event => "⚡",
            EconomyNodeKind.Pool => "▣",
            EconomyNodeKind.Gate => "◇",
            EconomyNodeKind.Queue => "≋",
            EconomyNodeKind.Register => "#",
            EconomyNodeKind.Converter => "⇄",
            EconomyNodeKind.Action => "▶",
            EconomyNodeKind.Sink => "−",
            _ => "•"
        };
    }
}
