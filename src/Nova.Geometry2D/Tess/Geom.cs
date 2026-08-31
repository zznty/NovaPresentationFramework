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
*/

namespace Nova.Geometry2D.Tess;

/// <summary>Geometric predicates over the sweep plane (s,t coordinates).</summary>
internal static class Geom
{
    public static bool IsWindingInside(WindingRule rule, int n)
    {
        return rule switch
        {
            WindingRule.EvenOdd => (n & 1) is 1,
            WindingRule.NonZero => n != 0,
            WindingRule.Positive => n > 0,
            WindingRule.Negative => n < 0,
            WindingRule.AbsGeqTwo => n is >= 2 or <= -2,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown winding rule."),
        };
    }

    public static bool VertCCW(MeshUtils.Vertex u, MeshUtils.Vertex v, MeshUtils.Vertex w)
    {
        double a = u.S * (v.T - w.T);
        double b = v.S * (w.T - u.T);
        double c = w.S * (u.T - v.T);
        return a + b + c >= 0.0;
    }

    public static bool VertEq(MeshUtils.Vertex lhs, MeshUtils.Vertex rhs)
    {
        return lhs.S == rhs.S && lhs.T == rhs.T;
    }

    public static bool VertLeq(MeshUtils.Vertex lhs, MeshUtils.Vertex rhs)
    {
        return lhs.S < rhs.S || (lhs.S == rhs.S && lhs.T <= rhs.T);
    }

    /// <summary>
    /// Given three vertices u,v,w such that VertLeq(u,v) &amp;&amp; VertLeq(v,w), evaluates the
    /// t-coord of edge uw at the s-coord of v: v.t - (uw)(v.s), the signed distance from uw
    /// to v. Numerically stable even when v is close to u or w.
    /// </summary>
    public static double EdgeEval(MeshUtils.Vertex u, MeshUtils.Vertex v, MeshUtils.Vertex w)
    {
        System.Diagnostics.Debug.Assert(VertLeq(u, v) && VertLeq(v, w));

        double gapL = v.S - u.S;
        double gapR = w.S - v.S;
        double gapSum = gapL + gapR;
        if (gapSum <= 0.0)
        {
            return 0; // vertical line
        }

        double ratio = gapL < gapR ? gapL / gapSum : gapR / gapSum;
        double baseT = gapL < gapR ? u.T - w.T : w.T - u.T;
        double vT = gapL < gapR ? v.T - u.T : v.T - w.T;
        return vT + (baseT * ratio);
    }

    /// <summary>Cheaper sign-matching variant of <see cref="EdgeEval"/>.</summary>
    public static double EdgeSign(MeshUtils.Vertex u, MeshUtils.Vertex v, MeshUtils.Vertex w)
    {
        System.Diagnostics.Debug.Assert(VertLeq(u, v) && VertLeq(v, w));

        double gapL = v.S - u.S;
        double gapR = w.S - v.S;
        double gapSum = gapL + gapR;
        if (gapSum <= 0.0)
        {
            return 0; // vertical line
        }

        double a = (v.T - w.T) * gapL;
        double b = (v.T - u.T) * gapR;
        return a + b;
    }

    public static bool TransLeq(MeshUtils.Vertex lhs, MeshUtils.Vertex rhs)
    {
        return lhs.T < rhs.T || (lhs.T == rhs.T && lhs.S <= rhs.S);
    }

    public static double TransEval(MeshUtils.Vertex u, MeshUtils.Vertex v, MeshUtils.Vertex w)
    {
        System.Diagnostics.Debug.Assert(TransLeq(u, v) && TransLeq(v, w));

        double gapL = v.T - u.T;
        double gapR = w.T - v.T;
        double gapSum = gapL + gapR;
        if (gapSum <= 0.0)
        {
            return 0; // vertical line
        }

        double ratio = gapL < gapR ? gapL / gapSum : gapR / gapSum;
        double baseS = gapL < gapR ? u.S - w.S : w.S - u.S;
        double vS = gapL < gapR ? v.S - u.S : v.S - w.S;
        return vS + (baseS * ratio);
    }

    public static double TransSign(MeshUtils.Vertex u, MeshUtils.Vertex v, MeshUtils.Vertex w)
    {
        System.Diagnostics.Debug.Assert(TransLeq(u, v) && TransLeq(v, w));

        double gapL = v.T - u.T;
        double gapR = w.T - v.T;
        double gapSum = gapL + gapR;
        if (gapSum <= 0.0)
        {
            return 0; // vertical line
        }

        double a = (v.S - w.S) * gapL;
        double b = (v.S - u.S) * gapR;
        return a + b;
    }

    public static bool EdgeGoesLeft(MeshUtils.Edge e)
    {
        return VertLeq(e.Dst!, e.Org!);
    }

    public static bool EdgeGoesRight(MeshUtils.Edge e)
    {
        return VertLeq(e.Org!, e.Dst!);
    }

    public static double VertL1dist(MeshUtils.Vertex u, MeshUtils.Vertex v)
    {
        return Math.Abs(u.S - v.S) + Math.Abs(u.T - v.T);
    }

    public static void AddWinding(MeshUtils.Edge eDst, MeshUtils.Edge eSrc)
    {
        eDst.Winding += eSrc.Winding;
        eDst.Sym!.Winding += eSrc.Sym!.Winding;
    }

    public static double Interpolate(double a, double x, double b, double y)
    {
        a = Math.Max(a, 0.0);
        b = Math.Max(b, 0.0);
        double weight = a / (a + b);
        return a > b
            ? y + ((x - y) * (b / (a + b)))
            : b == 0.0 ? (x + y) * 0.5 : x + ((y - x) * weight);
    }

    private static void Swap(ref MeshUtils.Vertex a, ref MeshUtils.Vertex b)
    {
        (a, b) = (b, a);
    }

    /// <summary>
    /// Computes the intersection of edges (o1,d1) and (o2,d2) into v, guaranteed to lie in
    /// the intersection of both edges' bounding rectangles. Numerically stable.
    /// </summary>
    public static void EdgeIntersect(MeshUtils.Vertex o1, MeshUtils.Vertex d1, MeshUtils.Vertex o2, MeshUtils.Vertex d2, MeshUtils.Vertex v)
    {
        if (!VertLeq(o1, d1))
        {
            Swap(ref o1, ref d1);
        }

        if (!VertLeq(o2, d2))
        {
            Swap(ref o2, ref d2);
        }

        if (!VertLeq(o1, o2))
        {
            Swap(ref o1, ref o2);
            Swap(ref d1, ref d2);
        }

        if (!VertLeq(o2, d1))
        {
            v.S = (o2.S + d1.S) / 2.0;
        }
        else if (VertLeq(d1, d2))
        {
            double z1 = EdgeEval(o1, o2, d1);
            double z2 = EdgeEval(o2, d1, d2);
            if (z1 + z2 < 0.0)
            {
                z1 = -z1;
                z2 = -z2;
            }

            v.S = Interpolate(z1, o2.S, z2, d1.S);
        }
        else
        {
            double z1 = EdgeSign(o1, o2, d1);
            double z2 = -EdgeSign(o1, d2, d1);
            if (z1 + z2 < 0.0)
            {
                z1 = -z1;
                z2 = -z2;
            }

            v.S = Interpolate(z1, o2.S, z2, d2.S);
        }

        if (!TransLeq(o1, d1))
        {
            Swap(ref o1, ref d1);
        }

        if (!TransLeq(o2, d2))
        {
            Swap(ref o2, ref d2);
        }

        if (!TransLeq(o1, o2))
        {
            Swap(ref o1, ref o2);
            Swap(ref d1, ref d2);
        }

        if (!TransLeq(o2, d1))
        {
            v.T = (o2.T + d1.T) / 2.0;
        }
        else if (TransLeq(d1, d2))
        {
            double z1 = TransEval(o1, o2, d1);
            double z2 = TransEval(o2, d1, d2);
            if (z1 + z2 < 0.0)
            {
                z1 = -z1;
                z2 = -z2;
            }

            v.T = Interpolate(z1, o2.T, z2, d1.T);
        }
        else
        {
            double z1 = TransSign(o1, o2, d1);
            double z2 = -TransSign(o1, d2, d1);
            if (z1 + z2 < 0.0)
            {
                z1 = -z1;
                z2 = -z2;
            }

            v.T = Interpolate(z1, o2.T, z2, d2.T);
        }
    }
}
