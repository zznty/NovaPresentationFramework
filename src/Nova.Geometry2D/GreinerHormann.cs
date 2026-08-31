using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Greiner-Hormann polygon clipping for boolean combine of two closed, flattened contours
/// that properly cross. Handles union/intersect/exclude/xor; polygons without crossings are
/// resolved by containment classification. Coincident edges are not modeled (the rect fast
/// path in <see cref="Combiner"/> covers the axis-aligned cases the WPF nests produce).
/// </summary>
internal static class GreinerHormann
{
    private const double Epsilon = 1e-9;

    private sealed class Node
    {
        public Point P;
        public Node? Prev;
        public Node? Next;
        public Node? Neighbor;
        public bool IsIntersection;
        public bool IsEntry;
        public bool Visited;
    }

    private readonly struct Candidate(int aEdge, int bEdge, double alphaA, double alphaB)
    {
        public int AEdge { get; } = aEdge;
        public int BEdge { get; } = bEdge;
        public double AlphaA { get; } = alphaA;
        public double AlphaB { get; } = alphaB;
    }

    public static void Combine(ReadOnlySpan<Point> a, ReadOnlySpan<Point> b, GeometryCombineMode mode, List<Contour> result)
    {
        Node[] headA = BuildRing(a);
        Node[] headB = BuildRing(b);

        if (!FindIntersections(headA, headB))
        {
            HandleNoCrossings(headA, headB, mode, result);
            return;
        }

        Classify(headA, headB);
        Classify(headB, headA);

        switch (mode)
        {
            case GeometryCombineMode.Union:
                TraceEntries(headA, backwardOnOther: false, result);
                TraceEntries(headB, backwardOnOther: false, result);
                break;

            case GeometryCombineMode.Intersect:
                TraceExits(headA, backwardOnOther: false, result);
                TraceExits(headB, backwardOnOther: false, result);
                break;

            case GeometryCombineMode.Exclude:
                TraceEntries(headA, backwardOnOther: true, result);
                break;

            case GeometryCombineMode.Xor:
                TraceEntries(headA, backwardOnOther: false, result);
                TraceExits(headA, backwardOnOther: false, result);
                TraceEntries(headB, backwardOnOther: false, result);
                TraceExits(headB, backwardOnOther: false, result);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown combine mode.");
        }
    }

    /// <summary>Builds a circular doubly-linked ring from the contour vertices.</summary>
    private static Node[] BuildRing(ReadOnlySpan<Point> contour)
    {
        int n = contour.Length;
        var nodes = new Node[n];
        for (int i = 0; i < n; i++)
        {
            nodes[i] = new Node { P = contour[i] };
        }

        for (int i = 0; i < n; i++)
        {
            nodes[i].Next = nodes[(i + 1) % n];
            nodes[i].Prev = nodes[(i + n - 1) % n];
        }

        return nodes;
    }

    /// <summary>Inserts proper-crossing intersection nodes into both rings. Returns false when none exist.</summary>
    private static bool FindIntersections(Node[] a, Node[] b)
    {
        var candidates = new List<Candidate>();
        int an = a.Length;
        int bn = b.Length;
        for (int i = 0; i < an; i++)
        {
            Point a1 = a[i].P;
            Point a2 = a[(i + 1) % an].P;
            for (int j = 0; j < bn; j++)
            {
                Point b1 = b[j].P;
                Point b2 = b[(j + 1) % bn].P;
                if (TryProperIntersect(a1, a2, b1, b2, out Point p, out double alphaA, out double alphaB))
                {
                    candidates.Add(new Candidate(i, j, alphaA, alphaB));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        // Pass 1: insert A-side nodes, per edge, in alpha order.
        var aNodes = new Node?[candidates.Count];
        for (int edge = 0; edge < an; edge++)
        {
            // Gather this edge's candidates in ascending alphaA.
            var onEdge = new List<(int Index, double Alpha)>();
            for (int k = 0; k < candidates.Count; k++)
            {
                if (candidates[k].AEdge == edge)
                {
                    onEdge.Add((k, candidates[k].AlphaA));
                }
            }

            if (onEdge.Count == 0)
            {
                continue;
            }

            onEdge.Sort(static (x, y) => x.Alpha.CompareTo(y.Alpha));
            Node insertPoint = a[edge];
            foreach ((int index, double alpha) in onEdge)
            {
                Point p = Interpolate(a[edge].P, a[(edge + 1) % an].P, alpha);
                var node = new Node { P = p, IsIntersection = true };
                InsertAfter(insertPoint, node);
                insertPoint = node;
                aNodes[index] = node;
            }
        }

        // Pass 2: insert B-side nodes, per edge, in alpha order, linking neighbors.
        for (int edge = 0; edge < bn; edge++)
        {
            var onEdge = new List<(int Index, double Alpha)>();
            for (int k = 0; k < candidates.Count; k++)
            {
                if (candidates[k].BEdge == edge)
                {
                    onEdge.Add((k, candidates[k].AlphaB));
                }
            }

            if (onEdge.Count == 0)
            {
                continue;
            }

            onEdge.Sort(static (x, y) => x.Alpha.CompareTo(y.Alpha));
            Node insertPoint = b[edge];
            foreach ((int index, double alpha) in onEdge)
            {
                Point p = Interpolate(b[edge].P, b[(edge + 1) % bn].P, alpha);
                var node = new Node { P = p, IsIntersection = true };
                InsertAfter(insertPoint, node);
                insertPoint = node;
                aNodes[index]!.Neighbor = node;
                node.Neighbor = aNodes[index];
            }
        }

        return true;
    }

    private static void InsertAfter(Node anchor, Node node)
    {
        Node? next = anchor.Next;
        anchor.Next = node;
        node.Prev = anchor;
        node.Next = next;
        next!.Prev = node;
    }

    /// <summary>Proper segment intersection (no collinear handling).</summary>
    private static bool TryProperIntersect(Point a1, Point a2, Point b1, Point b2, out Point point, out double alphaA, out double alphaB)
    {
        point = default;
        alphaA = 0;
        alphaB = 0;
        double d1 = Cross(b2.X - b1.X, b2.Y - b1.Y, a1.X - b1.X, a1.Y - b1.Y);
        double d2 = Cross(b2.X - b1.X, b2.Y - b1.Y, a2.X - b1.X, a2.Y - b1.Y);
        double d3 = Cross(a2.X - a1.X, a2.Y - a1.Y, b1.X - a1.X, b1.Y - a1.Y);
        double d4 = Cross(a2.X - a1.X, a2.Y - a1.Y, b2.X - a1.X, b2.Y - a1.Y);

        if (((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
            ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon)))
        {
            double dx = a2.X - a1.X;
            double dy = a2.Y - a1.Y;
            double denom = ((b2.X - b1.X) * dy) - ((b2.Y - b1.Y) * dx);
            if (Math.Abs(denom) < Epsilon)
            {
                return false;
            }

            double t = (((b1.X - a1.X) * (b2.Y - b1.Y)) - ((b1.Y - a1.Y) * (b2.X - b1.X))) / denom;
            point = new Point(a1.X + (t * dx), a1.Y + (t * dy));
            alphaA = t;
            double lenB = Math.Sqrt(((b2.X - b1.X) * (b2.X - b1.X)) + ((b2.Y - b1.Y) * (b2.Y - b1.Y)));
            alphaB = lenB < Epsilon ? 0 : Distance(point, b1) / lenB;
            return true;
        }

        return false;
    }

    /// <summary>Marks each intersection node entry/exit using the midpoint of its outgoing edge.</summary>
    private static void Classify(Node[] ring, Node[] other)
    {
        foreach (Node node in ring)
        {
            if (!node.IsIntersection)
            {
                continue;
            }

            Point outgoingMid = Midpoint(node.P, node.Next!.P);
            node.IsEntry = PointInPolygon(outgoingMid, other);
        }
    }

    private static void TraceEntries(Node[] ring, bool backwardOnOther, List<Contour> result)
    {
        foreach (Node start in ring)
        {
            if (!start.IsIntersection || !start.IsEntry || start.Visited || (start.Neighbor is { } nb && nb.Visited))
            {
                continue;
            }

            if (Trace(start, backwardOnOther) is { } contour && contour.Count >= 3)
            {
                result.Add(new Contour(isClosed: true, isFilled: true, contour));
            }
        }
    }

    private static void TraceExits(Node[] ring, bool backwardOnOther, List<Contour> result)
    {
        foreach (Node start in ring)
        {
            if (!start.IsIntersection || start.IsEntry || start.Visited || (start.Neighbor is { } nb && nb.Visited))
            {
                continue;
            }

            if (Trace(start, backwardOnOther) is { } contour && contour.Count >= 3)
            {
                result.Add(new Contour(isClosed: true, isFilled: true, contour));
            }
        }
    }

    /// <summary>
    /// Walks the result boundary from an intersection node, switching polygons at every
    /// intersection. Traversal is forward on the ring the walk starts on and on the other
    /// ring unless <paramref name="backwardOnOther"/> (exclude mode: the B ring is walked
    /// backward so the difference winds correctly). Returns the closed loop or null.
    /// </summary>
    private static List<Point>? Trace(Node start, bool backwardOnOther)
    {
        var loop = new List<Point>();
        Node node = start;
        bool onOther = false;
        while (!node.Visited)
        {
            node.Visited = true;
            loop.Add(node.P);
            if (node.IsIntersection && node.Neighbor is { } neighbor)
            {
                if (neighbor.Visited)
                {
                    break;
                }

                neighbor.Visited = true;
                onOther = !onOther;
                node = (onOther && backwardOnOther) ? neighbor.Prev! : neighbor.Next!;
            }
            else
            {
                node = node.Next!;
            }
        }

        return loop;
    }

    private static void HandleNoCrossings(Node[] a, Node[] b, GeometryCombineMode mode, List<Contour> result)
    {
        bool aInB = AllPointsInside(a, b);
        bool bInA = AllPointsInside(b, a);

        switch (mode)
        {
            case GeometryCombineMode.Union:
                if (aInB || bInA)
                {
                    result.Add(RingContour(aInB && bInA ? a : (bInA ? a : b)));
                }
                else
                {
                    result.Add(RingContour(a));
                    result.Add(RingContour(b));
                }

                break;

            case GeometryCombineMode.Intersect:
                if (aInB && bInA)
                {
                    result.Add(RingContour(a));
                }
                else if (aInB)
                {
                    result.Add(RingContour(a));
                }
                else if (bInA)
                {
                    result.Add(RingContour(b));
                }

                break;

            case GeometryCombineMode.Exclude:
                if (!bInA)
                {
                    result.Add(RingContour(a));
                }
                else if (!aInB)
                {
                    result.Add(RingContour(a));
                    result.Add(RingContour(b));
                }

                break;

            case GeometryCombineMode.Xor:
                if (aInB && bInA)
                {
                    // identical: empty
                }
                else if (aInB)
                {
                    result.Add(RingContour(b));
                    result.Add(RingContour(a));
                }
                else if (bInA)
                {
                    result.Add(RingContour(a));
                    result.Add(RingContour(b));
                }
                else
                {
                    result.Add(RingContour(a));
                    result.Add(RingContour(b));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown combine mode.");
        }
    }

    private static bool AllPointsInside(Node[] inner, Node[] outer)
    {
        foreach (Node node in inner)
        {
            if (!PointInPolygon(node.P, outer))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Even-odd point-in-polygon over the ring's vertex positions.</summary>
    private static bool PointInPolygon(Point p, Node[] ring)
    {
        int n = ring.Length;
        int winding = 0;
        for (int i = 0; i < n; i++)
        {
            Point a = ring[i].P;
            Point b = ring[(i + 1) % n].P;
            if (a.Y <= p.Y)
            {
                if (b.Y > p.Y && IsLeft(a, b, p) > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= p.Y && IsLeft(a, b, p) < 0)
            {
                winding--;
            }
        }

        return (winding & 1) != 0;
    }

    private static double IsLeft(Point a, Point b, Point p)
    {
        return ((b.X - a.X) * (p.Y - a.Y)) - ((p.X - a.X) * (b.Y - a.Y));
    }

    private static Contour RingContour(Node[] ring)
    {
        var contour = new Contour(isClosed: true);
        foreach (Node node in ring)
        {
            contour.Points.Add(node.P);
        }

        return contour;
    }

    private static Point Midpoint(Point a, Point b)
    {
        return new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
    }

    private static Point Interpolate(Point a, Point b, double t)
    {
        return new Point(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }

    private static double Cross(double ax, double ay, double bx, double by)
    {
        return (ax * by) - (ay * bx);
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
