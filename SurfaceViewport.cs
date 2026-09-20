using System;

namespace GraphCalculator
{
    public readonly record struct SurfaceViewport(
        double MinX,
        double MaxX,
        double MinY,
        double MaxY,
        double MinZ,
        double MaxZ)
    {
        public double Width => MaxX - MinX;
        public double Depth => MaxY - MinY;
        public double Height => MaxZ - MinZ;

        public SurfaceViewport WithZ(double minZ, double maxZ)
        {
            return new SurfaceViewport(MinX, MaxX, MinY, MaxY, minZ, maxZ);
        }

        public bool IsValid =>
            double.IsFinite(MinX) && double.IsFinite(MaxX) && MinX < MaxX
            && double.IsFinite(MinY) && double.IsFinite(MaxY) && MinY < MaxY
            && double.IsFinite(MinZ) && double.IsFinite(MaxZ) && MinZ < MaxZ;
    }
}
