using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Fill-rule hit testing over flattened contours. Backs the <c>MilUtility_PolygonHitTest</c>
/// / <c>MilUtility_PathGeometryHitTest</c> nests and geometry <c>FillContains</c>.
/// </summary>
[PublicAPI]
public static class PathFillContains
{
    /// <summary>Point-in-contour test honoring the fill rule (even-odd or nonzero winding).</summary>
    public static bool Contains(ReadOnlySpan<Point> contour, Point point, FillRule fillRule)
    {
        return Tessellator.Contains(contour, fillRule, point);
    }

    /// <summary>
    /// Point-in-path across all contours. In even-odd, every contour flips parity so holes
    /// (inner loops) cancel; in nonzero, the signed winding sums across contours.
    /// </summary>
    public static bool ContainsPath(ReadOnlySpan<Contour> contours, Point point, FillRule fillRule)
    {
        if (fillRule == FillRule.EvenOdd)
        {
            int parity = 0;
            foreach (Contour contour in contours)
            {
                if (Tessellator.Contains(contour.ReadOnlySpan, FillRule.EvenOdd, point))
                {
                    parity++;
                }
            }

            return (parity & 1) != 0;
        }

        int winding = 0;
        foreach (Contour contour in contours)
        {
            winding += Winding(contour.ReadOnlySpan, point);
        }

        return winding != 0;
    }

    private static int Winding(ReadOnlySpan<Point> contour, Point point)
    {
        int winding = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            Point a = contour[i];
            Point b = contour[(i + 1) % contour.Length];
            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && IsLeft(a, b, point) > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= point.Y && IsLeft(a, b, point) < 0)
            {
                winding--;
            }
        }

        return winding;
    }

    private static double IsLeft(Point a, Point b, Point p)
    {
        return ((b.X - a.X) * (p.Y - a.Y)) - ((p.X - a.X) * (b.Y - a.Y));
    }
}
