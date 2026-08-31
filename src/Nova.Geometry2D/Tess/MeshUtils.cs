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

/// <summary>Half-edge mesh nodes and topology primitives (Guibas/Stolfi quad-edge).</summary>
internal static class MeshUtils
{
    internal sealed class Vertex : IPooled<Vertex>
    {
        internal Vertex? Prev;
        internal Vertex? Next;
        internal Edge? AnEdge;

        internal Vec3 Coords;
        internal double S;
        internal double T;
        internal PQHandle PqHandle;
        internal int N;

        public void Init(TessPool pool)
        {
        }

        public void Reset(TessPool pool)
        {
            Prev = null;
            Next = null;
            AnEdge = null;
            Coords = Vec3.Zero;
            S = 0;
            T = 0;
            PqHandle = new PQHandle();
            N = 0;
        }
    }

    internal sealed class Face : IPooled<Face>
    {
        internal Face? Prev;
        internal Face? Next;
        internal Edge? AnEdge;

        internal Face? Trail;
        internal int N;
        internal bool Marked;
        internal bool Inside;

        internal int VertsCount
        {
            get
            {
                int n = 0;
                Edge eCur = AnEdge!;
                do
                {
                    n++;
                    eCur = eCur.Lnext!;
                }
                while (eCur != AnEdge);
                return n;
            }
        }

        public void Init(TessPool pool)
        {
        }

        public void Reset(TessPool pool)
        {
            Prev = null;
            Next = null;
            AnEdge = null;
            Trail = null;
            N = 0;
            Marked = false;
            Inside = false;
        }
    }

    internal sealed class Edge : IPooled<Edge>
    {
        internal Edge? PairE;
        internal Edge? PairESym;

        internal Edge? Next;
        internal Edge? Sym;
        internal Edge? Onext;
        internal Edge? Lnext;
        internal Vertex? Org;
        internal Face? Lface;
        internal Tess.ActiveRegion? ActiveRegion;
        internal int Winding;

        internal Face? Rface
        {
            get => Sym!.Lface;
            set => Sym!.Lface = value;
        }

        internal Vertex? Dst
        {
            get => Sym!.Org;
            set => Sym!.Org = value;
        }

        internal Edge? Oprev
        {
            get => Sym!.Lnext;
            set => Sym!.Lnext = value;
        }

        internal Edge? Lprev
        {
            get => Onext!.Sym;
            set => Onext!.Sym = value;
        }

        internal Edge? Dprev
        {
            get => Lnext!.Sym;
            set => Lnext!.Sym = value;
        }

        internal Edge? Rprev
        {
            get => Sym!.Onext;
            set => Sym!.Onext = value;
        }

        internal Edge? Dnext
        {
            get => Rprev!.Sym;
            set => Rprev!.Sym = value;
        }

        internal Edge? Rnext
        {
            get => Oprev!.Sym;
            set => Oprev!.Sym = value;
        }

        internal static void EnsureFirst(ref Edge e)
        {
            if (e == e.PairESym)
            {
                e = e.Sym!;
            }
        }

        public void Init(TessPool pool)
        {
        }

        public void Reset(TessPool pool)
        {
            PairE = null;
            PairESym = null;
            Next = null;
            Sym = null;
            Onext = null;
            Lnext = null;
            Org = null;
            Lface = null;
            ActiveRegion = null;
            Winding = 0;
        }
    }

    /// <summary>
    /// Splice(a, b) exchanges a.Onext and b.Onext. The Guibas/Stolfi quad-edge splice.
    /// </summary>
    public static void Splice(Edge a, Edge b)
    {
        Edge aOnext = a.Onext!;
        Edge bOnext = b.Onext!;

        aOnext.Sym!.Lnext = b;
        bOnext.Sym!.Lnext = a;
        a.Onext = bOnext;
        b.Onext = aOnext;
    }

    /// <summary>Attaches a new vertex as the origin of all edges in eOrig's vertex loop.</summary>
    public static void MakeVertex(TessPool pool, Edge eOrig, Vertex vNext)
    {
        Vertex vNew = pool.Get<Vertex>();

        // Insert before vNext in the circular doubly-linked list.
        Vertex vPrev = vNext.Prev!;
        vNew.Prev = vPrev;
        vPrev.Next = vNew;
        vNew.Next = vNext;
        vNext.Prev = vNew;

        vNew.AnEdge = eOrig;

        Edge e = eOrig;
        do
        {
            e.Org = vNew;
            e = e.Onext!;
        }
        while (e != eOrig);
    }

    /// <summary>Attaches a new face as the left face of all edges in eOrig's face loop.</summary>
    public static void MakeFace(TessPool pool, Edge eOrig, Face fNext)
    {
        Face fNew = pool.Get<Face>();

        // Insert before fNext in the circular doubly-linked list.
        Face fPrev = fNext.Prev!;
        fNew.Prev = fPrev;
        fPrev.Next = fNew;
        fNew.Next = fNext;
        fNext.Prev = fNew;

        fNew.AnEdge = eOrig;
        fNew.Trail = null;
        fNew.Marked = false;
        fNew.Inside = fNext.Inside;

        Edge e = eOrig;
        do
        {
            e.Lface = fNew;
            e = e.Lnext!;
        }
        while (e != eOrig);
    }

    /// <summary>Creates a new pair of half-edges forming their own loop.</summary>
    public static Edge MakeEdge(TessPool pool, Edge eNext)
    {
        System.Diagnostics.Debug.Assert(eNext != null);

        Edge e = pool.Get<Edge>();
        Edge eSym = pool.Get<Edge>();
        e.PairE = e;
        e.PairESym = eSym;
        eSym.PairE = e;
        eSym.PairESym = e;

        // Make sure eNext points to the first edge of the edge pair.
        Edge.EnsureFirst(ref eNext);

        // Insert before eNext; the prev pointer is stored in Sym.next.
        Edge ePrev = eNext.Sym!.Next!;
        eSym.Next = ePrev;
        ePrev.Sym!.Next = e;
        e.Next = eNext;
        eNext.Sym!.Next = eSym;

        e.Sym = eSym;
        e.Onext = e;
        e.Lnext = eSym;
        e.Org = null;
        e.Lface = null;
        e.Winding = 0;
        e.ActiveRegion = null;

        eSym.Sym = e;
        eSym.Onext = eSym;
        eSym.Lnext = e;
        eSym.Org = null;
        eSym.Lface = null;
        eSym.Winding = 0;
        eSym.ActiveRegion = null;

        return e;
    }

    /// <summary>Destroys an edge (both half-edges) and removes it from the global edge list.</summary>
    public static void KillEdge(TessPool pool, Edge eDel)
    {
        Edge.EnsureFirst(ref eDel);

        Edge eNext = eDel.Next!;
        Edge ePrev = eDel.Sym!.Next!;
        eNext.Sym!.Next = ePrev;
        ePrev.Sym!.Next = eNext;

        Edge? symNullable = eDel.Sym;
        pool.Return(ref symNullable);
        Edge? delNullable = eDel;
        pool.Return(ref delNullable);
    }

    /// <summary>Destroys a vertex, relinking its edge loop to newOrg.</summary>
    public static void KillVertex(TessPool pool, Vertex vDel, Vertex? newOrg)
    {
        Edge eStart = vDel.AnEdge!;

        Edge e = eStart;
        do
        {
            e.Org = newOrg;
            e = e.Onext!;
        }
        while (e != eStart);

        Vertex vPrev = vDel.Prev!;
        Vertex vNext = vDel.Next!;
        vNext.Prev = vPrev;
        vPrev.Next = vNext;

        Vertex? delNullable = vDel;
        pool.Return(ref delNullable);
    }

    /// <summary>Destroys a face, relinking its edge loop to newLFace.</summary>
    public static void KillFace(TessPool pool, Face fDel, Face? newLFace)
    {
        Edge eStart = fDel.AnEdge!;

        Edge e = eStart;
        do
        {
            e.Lface = newLFace;
            e = e.Lnext!;
        }
        while (e != eStart);

        Face fPrev = fDel.Prev!;
        Face fNext = fDel.Next!;
        fNext.Prev = fPrev;
        fPrev.Next = fNext;

        Face? delNullable = fDel;
        pool.Return(ref delNullable);
    }

    /// <summary>Signed area of a face in (s,t) sweep coordinates.</summary>
    public static double FaceArea(Face f)
    {
        double area = 0;
        Edge e = f.AnEdge!;
        do
        {
            area += (e.Org!.S - e.Dst!.S) * (e.Org.T + e.Dst.T);
            e = e.Lnext!;
        }
        while (e != f.AnEdge);
        return area;
    }
}
