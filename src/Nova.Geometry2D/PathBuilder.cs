using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>Builds a polyline path in double space. Curves are flattened on <see cref="Flatten"/>.</summary>
[PublicAPI]
public sealed class PathBuilder
{
    private const int MaxFlattenDepth = 32;
    private const double TwoThirds = 2.0 / 3.0;

    private readonly List<Figure> _figures = [];
    private Figure? _current;

    public void MoveTo(Point point)
    {
        FinalizeCurrent();
        _current = new Figure(point);
    }

    public void LineTo(Point point)
    {
        Figure figure = RequireFigure();
        figure.Segments.Add(new Segment(SegmentKind.Line, Point.Origin, Point.Origin, point));
    }

    public void QuadraticTo(Point control, Point endPoint)
    {
        Figure figure = RequireFigure();
        figure.Segments.Add(new Segment(SegmentKind.Quadratic, control, Point.Origin, endPoint));
    }

    public void CubicTo(Point control1, Point control2, Point endPoint)
    {
        Figure figure = RequireFigure();
        figure.Segments.Add(new Segment(SegmentKind.Cubic, control1, control2, endPoint));
    }

    public void Close()
    {
        if (_current is not { } figure)
        {
            return;
        }

        figure.IsClosed = true;
        FinalizeCurrent();
    }

    public void Flatten(double tolerance, ICollection<Point> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tolerance, 0.0);
        FinalizeCurrent();
        foreach (Figure figure in _figures)
        {
            AppendFigure(figure, tolerance, destination);
        }
    }

    private Figure RequireFigure()
    {
        return _current ?? throw new InvalidOperationException("The path has no current figure; call MoveTo first.");
    }

    private void FinalizeCurrent()
    {
        if (_current is { Segments.Count: > 0 } figure)
        {
            _figures.Add(figure);
        }
        _current = null;
    }

    private static void AppendFigure(Figure figure, double tolerance, ICollection<Point> destination)
    {
        if (figure.Segments.Count == 0)
        {
            return;
        }
        destination.Add(figure.Start);
        Point last = figure.Start;
        foreach (Segment segment in figure.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Line:
                    destination.Add(segment.End);
                    break;
                case SegmentKind.Quadratic:
                    FlattenQuadratic(last, segment.Control1, segment.End, tolerance, destination);
                    break;
                case SegmentKind.Cubic:
                    FlattenCubic(last, segment.Control1, segment.Control2, segment.End, tolerance, destination, 0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(figure), segment.Kind, "Unknown segment kind.");
            }
            last = segment.End;
        }
        if (figure.IsClosed && last != figure.Start)
        {
            destination.Add(figure.Start);
        }
    }

    private static void FlattenQuadratic(Point start, Point control, Point end, double tolerance, ICollection<Point> destination)
    {
        var control1 = new Point(start.X + ((control.X - start.X) * TwoThirds), start.Y + ((control.Y - start.Y) * TwoThirds));
        var control2 = new Point(end.X + ((control.X - end.X) * TwoThirds), end.Y + ((control.Y - end.Y) * TwoThirds));
        FlattenCubic(start, control1, control2, end, tolerance, destination, 0);
    }

    private static void FlattenCubic(Point p0, Point p1, Point p2, Point p3, double tolerance, ICollection<Point> destination, int depth)
    {
        while (true)
        {
            if (depth >= MaxFlattenDepth || IsFlat(p0, p1, p2, p3, tolerance))
            {
                destination.Add(p3);
                return;
            }

            Point p01 = MidPoint(p0, p1);
            Point p12 = MidPoint(p1, p2);
            Point p23 = MidPoint(p2, p3);
            Point p012 = MidPoint(p01, p12);
            Point p123 = MidPoint(p12, p23);
            Point p0123 = MidPoint(p012, p123);
            FlattenCubic(p0, p01, p012, p0123, tolerance, destination, depth + 1);
            p0 = p0123;
            p1 = p123;
            p2 = p23;
            depth++;
        }
    }

    private static bool IsFlat(Point p0, Point p1, Point p2, Point p3, double tolerance)
    {
        return DistanceToSegment(p1, p0, p3) <= tolerance && DistanceToSegment(p2, p0, p3) <= tolerance;
    }

    private static Point MidPoint(Point a, Point b)
    {
        return new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
        {
            double px = point.X - a.X;
            double py = point.Y - a.Y;
            return Math.Sqrt((px * px) + (py * py));
        }
        double t = (((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared;
        double cx = a.X + (Math.Clamp(t, 0.0, 1.0) * dx);
        double cy = a.Y + (Math.Clamp(t, 0.0, 1.0) * dy);
        double rx = point.X - cx;
        double ry = point.Y - cy;
        return Math.Sqrt((rx * rx) + (ry * ry));
    }

    private enum SegmentKind
    {
        Line,
        Quadratic,
        Cubic
    }

    private readonly struct Segment(SegmentKind kind, Point control1, Point control2, Point end)
    {
        public SegmentKind Kind { get; } = kind;
        public Point Control1 { get; } = control1;
        public Point Control2 { get; } = control2;
        public Point End { get; } = end;
    }

    private sealed class Figure(Point start)
    {
        public Point Start { get; } = start;
        public bool IsClosed { get; set; }
        public List<Segment> Segments { get; } = [];
    }
}
