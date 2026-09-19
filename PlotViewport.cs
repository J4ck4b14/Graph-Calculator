using System;

namespace GraphCalculator
{
    public readonly record struct PlotViewport(double MinX, double MaxX, double MinY, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public PlotViewport Pan(double xOffset, double yOffset)
        {
            return new PlotViewport(
                MinX + xOffset,
                MaxX + xOffset,
                MinY + yOffset,
                MaxY + yOffset);
        }

        public PlotViewport Zoom(double factor, double anchorX, double anchorY)
        {
            factor = Math.Clamp(factor, 0.05, 20.0);
            double newWidth = Width * factor;
            double newHeight = Height * factor;

            if (newWidth < 1e-12 || newHeight < 1e-12 || newWidth > 1e12 || newHeight > 1e12)
            {
                return this;
            }

            return new PlotViewport(
                anchorX + (MinX - anchorX) * factor,
                anchorX + (MaxX - anchorX) * factor,
                anchorY + (MinY - anchorY) * factor,
                anchorY + (MaxY - anchorY) * factor);
        }

        public PlotViewport WithY(double minY, double maxY)
        {
            return new PlotViewport(MinX, MaxX, minY, maxY);
        }
    }
}
