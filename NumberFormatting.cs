using System.Globalization;

namespace WpfTestApp
{
    public static class NumberFormatting
    {
        public static NumberFormatInfo SpaceGroupedDot()
        {
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberDecimalSeparator = ".";
            nfi.NumberGroupSeparator = " ";
            nfi.NumberGroupSizes = new[] { 3 };
            return nfi;
        }
    }
}