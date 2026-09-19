using System;
using System.Globalization;

namespace GraphCalculator
{
    public static class NumberFormatting
    {
        private static readonly NumberFormatInfo NumberFormat = CreateNumberFormat();

        public static string Format(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";
            if (value == 0) return "0";

            double magnitude = Math.Abs(value);
            if (magnitude < 1e-9 || magnitude >= 1e12)
            {
                return value.ToString("0.############E+0", CultureInfo.InvariantCulture);
            }

            return value.ToString("#,0.###############", NumberFormat);
        }

        public static string FormatAxis(double value)
        {
            if (value == 0) return "0";
            double magnitude = Math.Abs(value);

            if (magnitude < 1e-4 || magnitude >= 1e6)
            {
                return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
            }

            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static NumberFormatInfo CreateNumberFormat()
        {
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            format.NumberDecimalSeparator = ".";
            format.NumberGroupSeparator = " ";
            format.NumberGroupSizes = new[] { 3 };
            return format;
        }
    }
}
