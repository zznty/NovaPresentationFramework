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

/// <summary>3-component vector in double precision (the tessellator works on a sweep plane).</summary>
internal struct Vec3(double x, double y, double z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public double X = x;
    public double Y = y;
    public double Z = z;

    public double this[int index]
    {
        readonly get => index switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range."),
        };
        set
        {
            switch (index)
            {
                case 0:
                    X = value;
                    break;
                case 1:
                    Y = value;
                    break;
                case 2:
                    Z = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }
        }
    }

    public static void Sub(ref Vec3 lhs, ref Vec3 rhs, out Vec3 result)
    {
        result = new Vec3(lhs.X - rhs.X, lhs.Y - rhs.Y, lhs.Z - rhs.Z);
    }

    public static void Neg(ref Vec3 v)
    {
        v.X = -v.X;
        v.Y = -v.Y;
        v.Z = -v.Z;
    }

    public static void Dot(ref Vec3 u, ref Vec3 v, out double dot)
    {
        dot = (u.X * v.X) + (u.Y * v.Y) + (u.Z * v.Z);
    }

    public static void Normalize(ref Vec3 v)
    {
        double len = (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);
        System.Diagnostics.Debug.Assert(len >= 0.0);
        len = 1.0 / Math.Sqrt(len);
        v.X *= len;
        v.Y *= len;
        v.Z *= len;
    }

    public static int LongAxis(ref Vec3 v)
    {
        int i = 0;
        if (Math.Abs(v.Y) > Math.Abs(v.X))
        {
            i = 1;
        }

        if (Math.Abs(v.Z) > Math.Abs(i == 0 ? v.X : v.Y))
        {
            i = 2;
        }

        return i;
    }
}
