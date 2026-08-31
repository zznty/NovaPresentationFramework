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

/// <summary>Quad-edge mesh with splice/delete/connect topology operations.</summary>
internal sealed class Mesh : IPooled<Mesh>
{
    internal MeshUtils.Vertex? VHead;
    internal MeshUtils.Face? FHead;
    internal MeshUtils.Edge? EHead;
    internal MeshUtils.Edge? EHeadSym;

    public void Init(TessPool pool)
    {
        MeshUtils.Vertex v = VHead = pool.Get<MeshUtils.Vertex>();
        MeshUtils.Face f = FHead = pool.Get<MeshUtils.Face>();
        MeshUtils.Edge e = EHead = pool.Get<MeshUtils.Edge>();
        MeshUtils.Edge eSym = EHeadSym = pool.Get<MeshUtils.Edge>();
        e.PairE = e;
        e.PairESym = eSym;
        eSym.PairE = e;
        eSym.PairESym = e;

        v.Next = v.Prev = v;
        v.AnEdge = null;

        f.Next = f.Prev = f;
        f.AnEdge = null;
        f.Trail = null;
        f.Marked = false;
        f.Inside = false;

        e.Next = e;
        e.Sym = eSym;
        e.Onext = null;
        e.Lnext = null;
        e.Org = null;
        e.Lface = null;
        e.Winding = 0;
        e.ActiveRegion = null;

        eSym.Next = eSym;
        eSym.Sym = e;
        eSym.Onext = null;
        eSym.Lnext = null;
        eSym.Org = null;
        eSym.Lface = null;
        eSym.Winding = 0;
        eSym.ActiveRegion = null;
    }

    public void Reset(TessPool pool)
    {
        MeshUtils.Face? f = FHead;
        while (f is not null && f.Next is not null)
        {
            MeshUtils.Face? fNext = f.Next;
            pool.Return(ref f);
            f = fNext;
        }

        MeshUtils.Vertex? v = VHead;
        while (v is not null && v.Next is not null)
        {
            MeshUtils.Vertex? vNext = v.Next;
            pool.Return(ref v);
            v = vNext;
        }

        MeshUtils.Edge? e = EHead;
        while (e is not null && e.Next is not null)
        {
            MeshUtils.Edge? eNext = e.Next;
            pool.Return(ref e.Sym);
            pool.Return(ref e);
            e = eNext;
        }

        VHead = null;
        FHead = null;
        EHead = EHeadSym = null;
    }

    /// <summary>Creates one edge, two vertices and a loop (face).</summary>
    public MeshUtils.Edge MakeEdge(TessPool pool)
    {
        MeshUtils.Edge e = MeshUtils.MakeEdge(pool, EHead!);

        MeshUtils.MakeVertex(pool, e, VHead!);
        MeshUtils.MakeVertex(pool, e.Sym!, VHead!);
        MeshUtils.MakeFace(pool, e, FHead!);

        return e;
    }

    /// <summary>
    /// The basic mesh topology operation: swaps Onext pointers, merging or splitting
    /// vertices and faces as appropriate.
    /// </summary>
    public static void Splice(TessPool pool, MeshUtils.Edge eOrg, MeshUtils.Edge eDst)
    {
        if (eOrg == eDst)
        {
            return;
        }

        bool joiningVertices = false;
        if (eDst.Org != eOrg.Org)
        {
            joiningVertices = true;
            MeshUtils.KillVertex(pool, eDst.Org!, eOrg.Org);
        }

        bool joiningLoops = false;
        if (eDst.Lface != eOrg.Lface)
        {
            joiningLoops = true;
            MeshUtils.KillFace(pool, eDst.Lface!, eOrg.Lface);
        }

        MeshUtils.Splice(eDst, eOrg);

        if (!joiningVertices)
        {
            MeshUtils.MakeVertex(pool, eDst, eOrg.Org!);
            eOrg.Org!.AnEdge = eOrg;
        }

        if (!joiningLoops)
        {
            MeshUtils.MakeFace(pool, eDst, eOrg.Lface!);
            eOrg.Lface!.AnEdge = eOrg;
        }
    }

    /// <summary>Removes edge eDel, joining or splitting loops and deleting isolated elements.</summary>
    public static void Delete(TessPool pool, MeshUtils.Edge eDel)
    {
        MeshUtils.Edge eDelSym = eDel.Sym!;

        bool joiningLoops = false;
        if (eDel.Lface != eDel.Rface)
        {
            joiningLoops = true;
            MeshUtils.KillFace(pool, eDel.Lface!, eDel.Rface);
        }

        if (eDel.Onext == eDel)
        {
            MeshUtils.KillVertex(pool, eDel.Org!, null);
        }
        else
        {
            eDel.Rface!.AnEdge = eDel.Oprev;
            eDel.Org!.AnEdge = eDel.Onext;
            MeshUtils.Splice(eDel, eDel.Oprev!);

            if (!joiningLoops)
            {
                MeshUtils.MakeFace(pool, eDel, eDel.Lface!);
            }
        }

        if (eDelSym.Onext == eDelSym)
        {
            MeshUtils.KillVertex(pool, eDelSym.Org!, null);
            MeshUtils.KillFace(pool, eDelSym.Lface!, null);
        }
        else
        {
            eDel.Lface!.AnEdge = eDelSym.Oprev;
            eDelSym.Org!.AnEdge = eDelSym.Onext;
            MeshUtils.Splice(eDelSym, eDelSym.Oprev!);
        }

        MeshUtils.KillEdge(pool, eDel);
    }

    /// <summary>Creates eNew == eOrg.Lnext with a newly created Dst vertex.</summary>
    public static MeshUtils.Edge AddEdgeVertex(TessPool pool, MeshUtils.Edge eOrg)
    {
        MeshUtils.Edge eNew = MeshUtils.MakeEdge(pool, eOrg);
        MeshUtils.Edge eNewSym = eNew.Sym!;

        MeshUtils.Splice(eNew, eOrg.Lnext!);
        eNew.Org = eOrg.Dst;
        MeshUtils.MakeVertex(pool, eNewSym, eNew.Org!);
        eNew.Lface = eNewSym.Lface = eOrg.Lface;

        return eNew;
    }

    /// <summary>Splits eOrg into eOrg and eNew such that eNew == eOrg.Lnext.</summary>
    public static MeshUtils.Edge SplitEdge(TessPool pool, MeshUtils.Edge eOrg)
    {
        MeshUtils.Edge eTmp = AddEdgeVertex(pool, eOrg);
        MeshUtils.Edge eNew = eTmp.Sym!;

        MeshUtils.Splice(eOrg.Sym!, eOrg.Sym!.Oprev!);
        MeshUtils.Splice(eOrg.Sym!, eNew);

        eOrg.Dst = eNew.Org;
        eNew.Dst!.AnEdge = eNew.Sym;
        eNew.Rface = eOrg.Rface;
        eNew.Winding = eOrg.Winding;
        eNew.Sym!.Winding = eOrg.Sym!.Winding;

        return eNew;
    }

    /// <summary>Creates a new edge from eOrg.Dst to eDst.Org.</summary>
    public static MeshUtils.Edge Connect(TessPool pool, MeshUtils.Edge eOrg, MeshUtils.Edge eDst)
    {
        MeshUtils.Edge eNew = MeshUtils.MakeEdge(pool, eOrg);
        MeshUtils.Edge eNewSym = eNew.Sym!;

        bool joiningLoops = false;
        if (eDst.Lface != eOrg.Lface)
        {
            joiningLoops = true;
            MeshUtils.KillFace(pool, eDst.Lface!, eOrg.Lface);
        }

        MeshUtils.Splice(eNew, eOrg.Lnext!);
        MeshUtils.Splice(eNewSym, eDst);

        eNew.Org = eOrg.Dst;
        eNewSym.Org = eDst.Org;
        eNew.Lface = eNewSym.Lface = eOrg.Lface;
        eOrg.Lface!.AnEdge = eNewSym;

        if (!joiningLoops)
        {
            MeshUtils.MakeFace(pool, eNew, eOrg.Lface);
        }

        return eNew;
    }

    /// <summary>Destroys a face and its now-dangling edges.</summary>
    public static void ZapFace(TessPool pool, MeshUtils.Face fZap)
    {
        MeshUtils.Edge eStart = fZap.AnEdge!;

        MeshUtils.Edge eNext = eStart.Lnext!;
        MeshUtils.Edge e, eSym;
        do
        {
            e = eNext;
            eNext = e.Lnext!;

            e.Lface = null;
            if (e.Rface == null)
            {
                if (e.Onext == e)
                {
                    MeshUtils.KillVertex(pool, e.Org!, null);
                }
                else
                {
                    e.Org!.AnEdge = e.Onext;
                    MeshUtils.Splice(e, e.Oprev!);
                }

                eSym = e.Sym!;
                if (eSym.Onext == eSym)
                {
                    MeshUtils.KillVertex(pool, eSym.Org!, null);
                }
                else
                {
                    eSym.Org!.AnEdge = eSym.Onext;
                    MeshUtils.Splice(eSym, eSym.Oprev!);
                }

                MeshUtils.KillEdge(pool, e);
            }
        }
        while (e != eStart);

        MeshUtils.Face fPrev = fZap.Prev!;
        MeshUtils.Face fNext = fZap.Next!;
        fNext.Prev = fPrev;
        fPrev.Next = fNext;

        MeshUtils.Face? zapNullable = fZap;
        pool.Return(ref zapNullable);
    }

    [System.Diagnostics.Conditional("DEBUG")]
    public void Check()
    {
        MeshUtils.Edge? e;

        MeshUtils.Face? f = FHead?.Next;
        while (f is not null && f != FHead)
        {
            e = f.AnEdge;
            do
            {
                System.Diagnostics.Debug.Assert(e!.Sym != e);
                System.Diagnostics.Debug.Assert(e.Sym!.Sym == e);
                System.Diagnostics.Debug.Assert(e.Lnext!.Onext!.Sym == e);
                System.Diagnostics.Debug.Assert(e.Onext!.Sym!.Lnext == e);
                System.Diagnostics.Debug.Assert(e.Lface == f);
                e = e.Lnext;
            }
            while (e != f.AnEdge);
            f = f.Next;
        }

        MeshUtils.Vertex? v = VHead?.Next;
        while (v is not null && v != VHead)
        {
            e = v.AnEdge;
            do
            {
                System.Diagnostics.Debug.Assert(e!.Sym != e);
                System.Diagnostics.Debug.Assert(e.Sym!.Sym == e);
                System.Diagnostics.Debug.Assert(e.Lnext!.Onext!.Sym == e);
                System.Diagnostics.Debug.Assert(e.Onext!.Sym!.Lnext == e);
                System.Diagnostics.Debug.Assert(e.Org == v);
                e = e.Onext;
            }
            while (e != v.AnEdge);
            v = v.Next;
        }

        MeshUtils.Edge? ePrev = EHead;
        e = EHead?.Next;
        while (e is not null && e != EHead)
        {
            System.Diagnostics.Debug.Assert(e.Sym!.Next == ePrev!.Sym);
            System.Diagnostics.Debug.Assert(e.Sym != e);
            System.Diagnostics.Debug.Assert(e.Sym.Sym == e);
            System.Diagnostics.Debug.Assert(e.Org != null);
            System.Diagnostics.Debug.Assert(e.Dst != null);
            System.Diagnostics.Debug.Assert(e.Lnext!.Onext!.Sym == e);
            System.Diagnostics.Debug.Assert(e.Onext!.Sym!.Lnext == e);
            ePrev = e;
            e = e.Next;
        }
    }
}
