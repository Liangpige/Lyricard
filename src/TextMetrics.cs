using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Amc
{
    /// <summary>
    /// Text measurement that both the card and the board use, so "how wide is N characters" has
    /// exactly one answer. No visual tree needed: a Typeface is enough to lay text out.
    /// </summary>
    static class TextMetrics
    {
        const string Sample = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>Average advance width of a run of ordinary characters at this size.</summary>
        public static double AverageChar(double size, bool bold)
        {
            if (size <= 0.5) size = 12.0;
            var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(Sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                tf, size, Brushes.White, 1.0);
            return ft.WidthIncludingTrailingWhitespace / Sample.Length;
        }

        /// <summary>Box width for a module that may show <paramref name="chars"/> characters.</summary>
        public static double WidthForChars(int chars, double size, bool bold, double fallback)
        {
            if (chars <= 0) return fallback;
            double w = AverageChar(size, bold) * chars;
            return Math.Max(24.0, Math.Ceiling(w));
        }
    }
}
