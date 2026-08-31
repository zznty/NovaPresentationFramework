using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>
/// Projection math for 3D bounds on the managed WPF surface (the milcore
/// MIL3DCalcProjected2DBounds replacement). Pure double arithmetic — no WPF types —
/// so the vendored PresentationCore wraps its Matrix3D/Rect3D at the boundary.
/// </summary>
[PublicAPI]
public static class ProjectionBounds
{
    /// <summary>
    /// Projects the eight corners of an axis-aligned 3D box by a 4x4 matrix
    /// (row-vector convention: point' = point × matrix, the layout WPF's Matrix3D
    /// feeds milcore) and returns the axis-aligned 2D bounds of the projected
    /// corners, or <see cref="Rect.Empty"/> when the projection is degenerate.
    /// Corners on the camera plane (w == 0) are skipped. The matrix's z-row
    /// (m13/m23/m33) does not affect the 2D projection and is not part of the
    /// signature.
    /// </summary>
    public static Rect Compute(
        double m11, double m12, double m14,
        double m21, double m22, double m24,
        double m31, double m32, double m34,
        double offsetX, double offsetY, double m44,
        double boxX, double boxY, double boxZ,
        double sizeX, double sizeY, double sizeZ)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool any = false;

        for (int cx = 0; cx < 2; cx++)
        {
            double x = cx == 0 ? boxX : boxX + sizeX;
            for (int cy = 0; cy < 2; cy++)
            {
                double y = cy == 0 ? boxY : boxY + sizeY;
                for (int cz = 0; cz < 2; cz++)
                {
                    double z = cz == 0 ? boxZ : boxZ + sizeZ;

                    double w = (x * m14) + (y * m24) + (z * m34) + m44;
                    if (w == 0)
                    {
                        continue;
                    }

                    double px = ((x * m11) + (y * m21) + (z * m31) + offsetX) / w;
                    double py = ((x * m12) + (y * m22) + (z * m32) + offsetY) / w;
                    minX = Math.Min(minX, px);
                    minY = Math.Min(minY, py);
                    maxX = Math.Max(maxX, px);
                    maxY = Math.Max(maxY, py);
                    any = true;
                }
            }
        }

        return (!any || minX == maxX || minY == maxY)
            ? Rect.Empty
            : new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
