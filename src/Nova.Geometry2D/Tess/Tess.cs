/*
** SGI FREE SOFTWARE LICENSE B (Version 2.0, Sept. 18, 2008)
** Copyright (C) 2011 Silicon Graphics, Inc.
** All Rights Reserved.
**
** Permission is hereby granted, free of charge, to any person obtaining a copy
** of this software and associated documentation files (the "Software"), to deal
** in the Software without restriction, including without limitation the rights
** to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
** of the Software, and to permit persons to whom the Software is furnished to do so,
** subject to the following conditions:
**
** The above copyright notice including the dates of first publication and either this
** permission notice or a reference to http://oss.sgi.com/projects/FreeB/ shall be
** included in all copies or substantial portions of the Software.
**
** THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
** INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
** PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL SILICON GRAPHICS, INC.
** BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
** TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
** OR OTHER DEALINGS IN THE SOFTWARE.
**
** Except as contained in this notice, the name of Silicon Graphics, Inc. shall not
** be used in advertising or otherwise to promote the sale, use or other dealings in
** this Software without prior written authorization from Silicon Graphics, Inc.
*/
/*
** Original Author: Eric Veach, July 1994.
** libtess2: Mikko Mononen, http://code.google.com/p/libtess2/.
** LibTessDotNet: Remi Gillig, https://github.com/speps/LibTessDotNet
** Ported to Nova.Geometry2D: double precision, span contour input, direct triangle output.
*/

using Nova.Geometry;

namespace Nova.Geometry2D.Tess;

/// <summary>The winding rule determines how contours combine (OpenGL red book).</summary>
internal enum WindingRule
{
    EvenOdd,
    NonZero,
    Positive,
    Negative,
    AbsGeqTwo,
}

/// <summary>
/// Sweep-line tessellator (GLU/libtess2 algorithm). Handles self-intersecting contours,
/// coincident vertices and holes under any winding rule. Contours are added as double
/// precision spans; output is written directly into a triangle span.
/// </summary>
internal sealed partial class Tess
{
    private readonly TessPool _pool;
    private Mesh? _mesh;
    private Vec3 _normal;
    private Vec3 _sUnit;
    private Vec3 _tUnit;

    private double _bminX;
    private double _bminY;
    private double _bmaxX;
    private double _bmaxY;

    private WindingRule _windingRule;

    private Dict<ActiveRegion>? _dict;
    private PriorityQueue<MeshUtils.Vertex>? _pq;
    private MeshUtils.Vertex? _event;

    internal const double SUnitX = 1;
    internal const double SUnitY = 0;
    internal double SentinelCoord = 4e150;

    internal Tess()
    {
        _pool = new TessPool();
        _normal = Vec3.Zero;
        _bminX = _bminY = _bmaxX = _bmaxY = 0;
        _windingRule = WindingRule.EvenOdd;
        _mesh = null;
    }

    private void ComputeNormal(ref Vec3 norm)
    {
        MeshUtils.Vertex v = _mesh!.VHead!.Next!;

        var minVal = new double[3] { v.Coords.X, v.Coords.Y, v.Coords.Z };
        var minVert = new MeshUtils.Vertex[3] { v, v, v };
        var maxVal = new double[3] { v.Coords.X, v.Coords.Y, v.Coords.Z };
        var maxVert = new MeshUtils.Vertex[3] { v, v, v };

        for (; v != _mesh.VHead; v = v.Next!)
        {
            if (v.Coords.X < minVal[0])
            {
                minVal[0] = v.Coords.X;
                minVert[0] = v;
            }

            if (v.Coords.Y < minVal[1])
            {
                minVal[1] = v.Coords.Y;
                minVert[1] = v;
            }

            if (v.Coords.Z < minVal[2])
            {
                minVal[2] = v.Coords.Z;
                minVert[2] = v;
            }

            if (v.Coords.X > maxVal[0])
            {
                maxVal[0] = v.Coords.X;
                maxVert[0] = v;
            }

            if (v.Coords.Y > maxVal[1])
            {
                maxVal[1] = v.Coords.Y;
                maxVert[1] = v;
            }

            if (v.Coords.Z > maxVal[2])
            {
                maxVal[2] = v.Coords.Z;
                maxVert[2] = v;
            }
        }

        int i = 0;
        if (maxVal[1] - minVal[1] > maxVal[0] - minVal[0])
        {
            i = 1;
        }

        if (maxVal[2] - minVal[2] > maxVal[i] - minVal[i])
        {
            i = 2;
        }

        if (minVal[i] >= maxVal[i])
        {
            norm = new Vec3(0, 0, 1);
            return;
        }

        double maxLen2 = 0;
        MeshUtils.Vertex v1 = minVert[i];
        MeshUtils.Vertex v2 = maxVert[i];
        Vec3.Sub(ref v1.Coords, ref v2.Coords, out Vec3 d1);
        for (v = _mesh.VHead!.Next!; v != _mesh.VHead; v = v.Next!)
        {
            Vec3.Sub(ref v.Coords, ref v2.Coords, out Vec3 d2);
            Vec3 tNorm = new Vec3((d1.Y * d2.Z) - (d1.Z * d2.Y), (d1.Z * d2.X) - (d1.X * d2.Z), (d1.X * d2.Y) - (d1.Y * d2.X));
            double tLen2 = (tNorm.X * tNorm.X) + (tNorm.Y * tNorm.Y) + (tNorm.Z * tNorm.Z);
            if (tLen2 > maxLen2)
            {
                maxLen2 = tLen2;
                norm = tNorm;
            }
        }

        if (maxLen2 <= 0.0)
        {
            norm = Vec3.Zero;
            i = Vec3.LongAxis(ref d1);
            norm[i] = 1;
        }
    }

    private void CheckOrientation()
    {
        double area = 0.0;
        for (MeshUtils.Face f = _mesh!.FHead!.Next!; f != _mesh.FHead; f = f.Next!)
        {
            if (f.AnEdge!.Winding <= 0)
            {
                continue;
            }

            area += MeshUtils.FaceArea(f);
        }

        if (area < 0.0)
        {
            for (MeshUtils.Vertex v = _mesh.VHead!.Next!; v != _mesh.VHead; v = v.Next!)
            {
                v.T = -v.T;
            }

            Vec3.Neg(ref _tUnit);
        }
    }

    private void ProjectPolygon()
    {
        Vec3 norm = _normal;

        bool computedNormal = false;
        if (norm.X == 0.0 && norm.Y == 0.0 && norm.Z == 0.0)
        {
            ComputeNormal(ref norm);
            _normal = norm;
            computedNormal = true;
        }

        int i = Vec3.LongAxis(ref norm);

        _sUnit[i] = 0;
        _sUnit[(i + 1) % 3] = SUnitX;
        _sUnit[(i + 2) % 3] = SUnitY;

        _tUnit[i] = 0;
        _tUnit[(i + 1) % 3] = norm[i] > 0.0 ? -SUnitY : SUnitY;
        _tUnit[(i + 2) % 3] = norm[i] > 0.0 ? SUnitX : -SUnitX;

        for (MeshUtils.Vertex v = _mesh!.VHead!.Next!; v != _mesh.VHead; v = v.Next!)
        {
            Vec3.Dot(ref v.Coords, ref _sUnit, out v.S);
            Vec3.Dot(ref v.Coords, ref _tUnit, out v.T);
        }

        if (computedNormal)
        {
            CheckOrientation();
        }

        bool first = true;
        for (MeshUtils.Vertex v = _mesh.VHead!.Next!; v != _mesh.VHead; v = v.Next!)
        {
            if (first)
            {
                _bminX = _bmaxX = v.S;
                _bminY = _bmaxY = v.T;
                first = false;
            }
            else
            {
                if (v.S < _bminX)
                {
                    _bminX = v.S;
                }

                if (v.S > _bmaxX)
                {
                    _bmaxX = v.S;
                }

                if (v.T < _bminY)
                {
                    _bminY = v.T;
                }

                if (v.T > _bmaxY)
                {
                    _bmaxY = v.T;
                }
            }
        }
    }

    /// <summary>
    /// Tessellates a monotone region (a single CCW loop of half-edges, monotone in s).
    /// The region is split into triangles by adding interior edges.
    /// </summary>
    private void TessellateMonoRegion(MeshUtils.Face face)
    {
        MeshUtils.Edge up = face.AnEdge!;
        System.Diagnostics.Debug.Assert(up.Lnext != up && up.Lnext!.Lnext != up);

        while (Geom.VertLeq(up.Dst!, up.Org!))
        {
            up = up.Lprev!;
        }

        while (Geom.VertLeq(up.Org!, up.Dst!))
        {
            up = up.Lnext!;
        }

        MeshUtils.Edge lo = up.Lprev!;

        while (up.Lnext != lo)
        {
            if (Geom.VertLeq(up.Dst!, lo.Org!))
            {
                while (lo.Lnext != up && (Geom.EdgeGoesLeft(lo.Lnext!) || Geom.EdgeSign(lo.Org!, lo.Dst!, lo.Lnext!.Dst!) <= 0.0))
                {
                    lo = Mesh.Connect(_pool, lo.Lnext!, lo).Sym!;
                }

                lo = lo.Lprev!;
            }
            else
            {
                while (lo.Lnext != up && (Geom.EdgeGoesRight(up.Lprev!) || Geom.EdgeSign(up.Dst!, up.Org!, up.Lprev!.Org!) >= 0.0))
                {
                    up = Mesh.Connect(_pool, up, up.Lprev!).Sym!;
                }

                up = up.Lnext!;
            }
        }

        System.Diagnostics.Debug.Assert(lo.Lnext != up);
        while (lo.Lnext!.Lnext != up)
        {
            lo = Mesh.Connect(_pool, lo.Lnext, lo).Sym!;
        }
    }

    private void TessellateInterior()
    {
        MeshUtils.Face f, next;
        for (f = _mesh!.FHead!.Next!; f != _mesh.FHead; f = next)
        {
            next = f.Next!;
            if (f.Inside)
            {
                TessellateMonoRegion(f);
            }
        }
    }

    /// <summary>Adds a closed contour (a span of 2D points).</summary>
    public void AddContour(ReadOnlySpan<Point> vertices)
    {
        _mesh ??= _pool.Get<Mesh>();

        MeshUtils.Edge? e = null;
        for (int i = 0; i < vertices.Length; ++i)
        {
            if (e is null)
            {
                e = _mesh.MakeEdge(_pool);
                Mesh.Splice(_pool, e, e.Sym!);
            }
            else
            {
                _ = Mesh.SplitEdge(_pool, e);
                e = e.Lnext!;
            }

            Point p = vertices[i];
            e.Org!.Coords = new Vec3(p.X, p.Y, 0);

            e.Winding = 1;
            e.Sym!.Winding = -1;
        }
    }

    /// <summary>
    /// Runs the sweep and returns the number of triangle points the tessellation would
    /// produce, without writing any output. Used by callers to size their destination.
    /// </summary>
    public int TessellateCount(WindingRule windingRule = WindingRule.EvenOdd)
    {
        _windingRule = windingRule;
        if (_mesh is null)
        {
            return 0;
        }

        ProjectPolygon();
        ComputeInterior();
        TessellateInterior();
        _mesh.Check();

        int faceCount = 0;
        for (MeshUtils.Face f = _mesh.FHead!.Next!; f != _mesh.FHead; f = f.Next!)
        {
            if (f.Inside)
            {
                faceCount++;
            }
        }

        _pool.Return(ref _mesh);
        return faceCount * 3;
    }

    /// <summary>
    /// Runs the sweep and writes the resulting triangles (3 points each) into
    /// <paramref name="destination"/>. Returns the number of points written. Throws when the
    /// destination is too small.
    /// </summary>
    public int Tessellate(Span<Point> destination, WindingRule windingRule = WindingRule.EvenOdd)
    {
        _windingRule = windingRule;
        if (_mesh is null)
        {
            return 0;
        }

        ProjectPolygon();
        ComputeInterior();
        TessellateInterior();
        _mesh.Check();

        // First pass: count inside faces to size the output.
        int faceCount = 0;
        for (MeshUtils.Face f = _mesh.FHead!.Next!; f != _mesh.FHead; f = f.Next!)
        {
            if (f.Inside)
            {
                faceCount++;
            }
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(faceCount * 3, destination.Length, nameof(destination));

        // Second pass: emit each inside face's vertices as a triangle.
        int written = 0;
        for (MeshUtils.Face f = _mesh.FHead!.Next!; f != _mesh.FHead; f = f.Next!)
        {
            if (!f.Inside)
            {
                continue;
            }

            MeshUtils.Edge e = f.AnEdge!;
            do
            {
                destination[written++] = new Point(e.Org!.Coords.X, e.Org.Coords.Y);
                e = e.Lnext!;
            }
            while (e != f.AnEdge);
        }

        _pool.Return(ref _mesh);
        return written;
    }
}
