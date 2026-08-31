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

internal sealed partial class Tess
{
    internal sealed class ActiveRegion : IPooled<ActiveRegion>
    {
        internal MeshUtils.Edge? EUp;
        internal Dict<ActiveRegion>.Node? NodeUp;
        internal int WindingNumber;
        internal bool Inside;
        internal bool Sentinel;
        internal bool Dirty;
        internal bool FixUpperEdge;

        public void Init(TessPool pool)
        {
        }

        public void Reset(TessPool pool)
        {
            EUp = null;
            NodeUp = null; // the Dict owns its nodes
            WindingNumber = 0;
            Inside = false;
            Sentinel = false;
            Dirty = false;
            FixUpperEdge = false;
        }
    }

    private static ActiveRegion RegionBelow(ActiveRegion reg)
    {
        return reg.NodeUp!.Prev!.Key!;
    }

    private static ActiveRegion RegionAbove(ActiveRegion reg)
    {
        return reg.NodeUp!.Next!.Key!;
    }

    /// <summary>Orders two active regions' upper edges at the sweep line (stable t-evaluation).</summary>
    private bool EdgeLeq(ActiveRegion reg1, ActiveRegion reg2)
    {
        MeshUtils.Edge e1 = reg1.EUp!;
        MeshUtils.Edge e2 = reg2.EUp!;

        return e1.Dst == _event
            ? e2.Dst == _event
                ? Geom.VertLeq(e1.Org!, e2.Org!)
                    ? Geom.EdgeSign(e2.Dst!, e1.Org!, e2.Org!) <= 0.0
                    : Geom.EdgeSign(e1.Dst!, e2.Org!, e1.Org!) >= 0.0
                : Geom.EdgeSign(e2.Dst!, _event!, e2.Org!) <= 0.0
            : e2.Dst == _event
                ? Geom.EdgeSign(e1.Dst!, _event!, e1.Org!) >= 0.0
                : Geom.EdgeEval(e1.Dst!, _event!, e1.Org!) >= Geom.EdgeEval(e2.Dst!, _event!, e2.Org!);
    }

    private void DeleteRegion(ActiveRegion reg)
    {
        if (reg.FixUpperEdge)
        {
            System.Diagnostics.Debug.Assert(reg.EUp!.Winding == 0);
        }

        reg.EUp!.ActiveRegion = null;
        _dict!.Remove(reg.NodeUp!);
        ActiveRegion? nullable = reg;
        _pool.Return(ref nullable);
    }

    private void FixUpperEdge(ActiveRegion reg, MeshUtils.Edge newEdge)
    {
        System.Diagnostics.Debug.Assert(reg.FixUpperEdge);
        Mesh.Delete(_pool, reg.EUp!);
        reg.FixUpperEdge = false;
        reg.EUp = newEdge;
        newEdge.ActiveRegion = reg;
    }

    private ActiveRegion TopLeftRegion(ActiveRegion reg)
    {
        MeshUtils.Vertex org = reg.EUp!.Org!;

        do
        {
            reg = RegionAbove(reg);
        }
        while (reg.EUp!.Org == org);

        if (reg.FixUpperEdge)
        {
            MeshUtils.Edge e = Mesh.Connect(_pool, RegionBelow(reg).EUp!.Sym!, reg.EUp.Lnext!);
            FixUpperEdge(reg, e);
            reg = RegionAbove(reg);
        }

        return reg;
    }

    private static ActiveRegion TopRightRegion(ActiveRegion reg)
    {
        MeshUtils.Vertex dst = reg.EUp!.Dst!;

        do
        {
            reg = RegionAbove(reg);
        }
        while (reg.EUp!.Dst == dst);

        return reg;
    }

    private ActiveRegion AddRegionBelow(ActiveRegion regAbove, MeshUtils.Edge eNewUp)
    {
        ActiveRegion regNew = _pool.Get<ActiveRegion>();

        regNew.EUp = eNewUp;
        regNew.NodeUp = _dict!.InsertBefore(regAbove.NodeUp!, regNew);
        regNew.FixUpperEdge = false;
        regNew.Sentinel = false;
        regNew.Dirty = false;

        eNewUp.ActiveRegion = regNew;

        return regNew;
    }

    private void ComputeWinding(ActiveRegion reg)
    {
        reg.WindingNumber = RegionAbove(reg).WindingNumber + reg.EUp!.Winding;
        reg.Inside = Geom.IsWindingInside(_windingRule, reg.WindingNumber);
    }

    private void FinishRegion(ActiveRegion reg)
    {
        MeshUtils.Edge e = reg.EUp!;
        MeshUtils.Face f = e.Lface!;

        f.Inside = reg.Inside;
        f.AnEdge = e;
        DeleteRegion(reg);
    }

    private MeshUtils.Edge FinishLeftRegions(ActiveRegion regFirst, ActiveRegion? regLast)
    {
        ActiveRegion regPrev = regFirst;
        MeshUtils.Edge ePrev = regFirst.EUp!;

        while (regPrev != regLast)
        {
            regPrev.FixUpperEdge = false;
            ActiveRegion reg = RegionBelow(regPrev);
            MeshUtils.Edge e = reg.EUp!;
            if (e.Org != ePrev.Org)
            {
                if (!reg.FixUpperEdge)
                {
                    FinishRegion(regPrev);
                    break;
                }

                e = Mesh.Connect(_pool, ePrev.Lprev!, e.Sym!);
                FixUpperEdge(reg, e);
            }

            if (ePrev.Onext != e)
            {
                Mesh.Splice(_pool, e.Oprev!, e);
                Mesh.Splice(_pool, ePrev, e);
            }

            FinishRegion(regPrev);
            ePrev = reg.EUp!;
            regPrev = reg;
        }

        return ePrev;
    }

    private void AddRightEdges(ActiveRegion regUp, MeshUtils.Edge eFirst, MeshUtils.Edge eLast, MeshUtils.Edge? eTopLeft, bool cleanUp)
    {
        bool firstTime = true;

        MeshUtils.Edge e = eFirst;
        do
        {
            System.Diagnostics.Debug.Assert(Geom.VertLeq(e.Org!, e.Dst!));
            _ = AddRegionBelow(regUp, e.Sym!);
            e = e.Onext!;
        }
        while (e != eLast);

        eTopLeft ??= RegionBelow(regUp).EUp!.Rprev!;

        ActiveRegion regPrev = regUp;
        MeshUtils.Edge ePrev = eTopLeft;
        while (true)
        {
            ActiveRegion reg = RegionBelow(regPrev);
            e = reg.EUp!.Sym!;
            if (e.Org != ePrev.Org)
            {
                break;
            }

            if (e.Onext != ePrev)
            {
                Mesh.Splice(_pool, e.Oprev!, e);
                Mesh.Splice(_pool, ePrev.Oprev!, e);
            }

            reg.WindingNumber = regPrev.WindingNumber - e.Winding;
            reg.Inside = Geom.IsWindingInside(_windingRule, reg.WindingNumber);

            regPrev.Dirty = true;
            if (!firstTime && CheckForRightSplice(regPrev))
            {
                Geom.AddWinding(e, ePrev);
                DeleteRegion(regPrev);
                Mesh.Delete(_pool, ePrev);
            }

            firstTime = false;
            regPrev = reg;
            ePrev = e;
        }

        regPrev.Dirty = true;
        System.Diagnostics.Debug.Assert(regPrev.WindingNumber - e.Winding == RegionBelow(regPrev).WindingNumber);

        if (cleanUp)
        {
            WalkDirtyRegions(regPrev);
        }
    }

    private void SpliceMergeVertices(MeshUtils.Edge e1, MeshUtils.Edge e2)
    {
        Mesh.Splice(_pool, e1, e2);
    }

    /// <summary>Interpolates the original-space coordinates of an intersection vertex.</summary>
    private static void GetIntersectData(MeshUtils.Vertex isect, MeshUtils.Vertex orgUp, MeshUtils.Vertex dstUp, MeshUtils.Vertex orgLo, MeshUtils.Vertex dstLo)
    {
        isect.Coords = Vec3.Zero;
        VertexWeights(isect, orgUp, dstUp, out _, out _);
        VertexWeights(isect, orgLo, dstLo, out _, out _);
    }

    private static void VertexWeights(MeshUtils.Vertex isect, MeshUtils.Vertex org, MeshUtils.Vertex dst, out double w0, out double w1)
    {
        double t1 = Geom.VertL1dist(org, isect);
        double t2 = Geom.VertL1dist(dst, isect);

        double weightSum = t1 + t2;
        w0 = t2 / weightSum / 2.0;
        w1 = t1 / weightSum / 2.0;

        double x0 = w0 * org.Coords.X;
        double x1 = w1 * dst.Coords.X;
        double y0 = w0 * org.Coords.Y;
        double y1 = w1 * dst.Coords.Y;
        double z0 = w0 * org.Coords.Z;
        double z1 = w1 * dst.Coords.Z;
        isect.Coords.X += x0 + x1;
        isect.Coords.Y += y0 + y1;
        isect.Coords.Z += z0 + z1;
    }

    private bool CheckForRightSplice(ActiveRegion regUp)
    {
        ActiveRegion regLo = RegionBelow(regUp);
        MeshUtils.Edge eUp = regUp.EUp!;
        MeshUtils.Edge eLo = regLo.EUp!;

        if (Geom.VertLeq(eUp.Org!, eLo.Org!))
        {
            if (Geom.EdgeSign(eLo.Dst!, eUp.Org!, eLo.Org!) > 0.0)
            {
                return false;
            }

            if (!Geom.VertEq(eUp.Org!, eLo.Org!))
            {
                _ = Mesh.SplitEdge(_pool, eLo.Sym!);
                Mesh.Splice(_pool, eUp, eLo.Oprev!);
                regUp.Dirty = true;
                regLo.Dirty = true;
            }
            else if (eUp.Org != eLo.Org)
            {
                _pq!.Remove(eUp.Org!.PqHandle);
                SpliceMergeVertices(eLo.Oprev!, eUp);
            }
        }
        else
        {
            if (Geom.EdgeSign(eUp.Dst!, eLo.Org!, eUp.Org!) < 0.0)
            {
                return false;
            }

            RegionAbove(regUp).Dirty = true;
            regUp.Dirty = true;
            _ = Mesh.SplitEdge(_pool, eUp.Sym!);
            Mesh.Splice(_pool, eLo.Oprev!, eUp);
        }

        return true;
    }

    private bool CheckForLeftSplice(ActiveRegion regUp)
    {
        ActiveRegion regLo = RegionBelow(regUp);
        MeshUtils.Edge eUp = regUp.EUp!;
        MeshUtils.Edge eLo = regLo.EUp!;

        System.Diagnostics.Debug.Assert(!Geom.VertEq(eUp.Dst!, eLo.Dst!));

        if (Geom.VertLeq(eUp.Dst!, eLo.Dst!))
        {
            if (Geom.EdgeSign(eUp.Dst!, eLo.Dst!, eUp.Org!) < 0.0)
            {
                return false;
            }

            RegionAbove(regUp).Dirty = true;
            regUp.Dirty = true;
            MeshUtils.Edge e = Mesh.SplitEdge(_pool, eUp);
            Mesh.Splice(_pool, eLo.Sym!, e);
            e.Lface!.Inside = regUp.Inside;
        }
        else
        {
            if (Geom.EdgeSign(eLo.Dst!, eUp.Dst!, eLo.Org!) > 0.0)
            {
                return false;
            }

            regUp.Dirty = true;
            regLo.Dirty = true;
            MeshUtils.Edge e = Mesh.SplitEdge(_pool, eLo);
            Mesh.Splice(_pool, eUp.Lnext!, eLo.Sym!);
            e.Rface!.Inside = regUp.Inside;
        }

        return true;
    }

    private bool CheckForIntersect(ActiveRegion regUp)
    {
        ActiveRegion regLo = RegionBelow(regUp);
        MeshUtils.Edge eUp = regUp.EUp!;
        MeshUtils.Edge eLo = regLo.EUp!;
        MeshUtils.Vertex orgUp = eUp.Org!;
        MeshUtils.Vertex orgLo = eLo.Org!;
        MeshUtils.Vertex dstUp = eUp.Dst!;
        MeshUtils.Vertex dstLo = eLo.Dst!;

        System.Diagnostics.Debug.Assert(!Geom.VertEq(dstLo, dstUp));
        System.Diagnostics.Debug.Assert(Geom.EdgeSign(dstUp, _event!, orgUp) <= 0.0);
        System.Diagnostics.Debug.Assert(Geom.EdgeSign(dstLo, _event!, orgLo) >= 0.0);
        System.Diagnostics.Debug.Assert(orgUp != _event && orgLo != _event);
        System.Diagnostics.Debug.Assert(!regUp.FixUpperEdge && !regLo.FixUpperEdge);

        if (orgUp == orgLo)
        {
            return false;
        }

        double tMinUp = Math.Min(orgUp.T, dstUp.T);
        double tMaxLo = Math.Max(orgLo.T, dstLo.T);
        if (tMinUp > tMaxLo)
        {
            return false;
        }

        if (Geom.VertLeq(orgUp, orgLo))
        {
            if (Geom.EdgeSign(dstLo, orgUp, orgLo) > 0.0)
            {
                return false;
            }
        }
        else if (Geom.EdgeSign(dstUp, orgLo, orgUp) < 0.0)
        {
            return false;
        }

        MeshUtils.Vertex? isect = _pool.Get<MeshUtils.Vertex>();
        Geom.EdgeIntersect(dstUp, orgUp, dstLo, orgLo, isect);

        if (Geom.VertLeq(isect, _event!))
        {
            isect.S = _event!.S;
            isect.T = _event.T;
        }

        MeshUtils.Vertex orgMin = Geom.VertLeq(orgUp, orgLo) ? orgUp : orgLo;
        if (Geom.VertLeq(orgMin, isect))
        {
            isect.S = orgMin.S;
            isect.T = orgMin.T;
        }

        if (Geom.VertEq(isect, orgUp) || Geom.VertEq(isect, orgLo))
        {
            _ = CheckForRightSplice(regUp);
            _pool.Return(ref isect);
            return false;
        }

        if ((!Geom.VertEq(dstUp, _event!) && Geom.EdgeSign(dstUp, _event!, isect!) >= 0.0)
            || (!Geom.VertEq(dstLo, _event!) && Geom.EdgeSign(dstLo, _event!, isect!) <= 0.0))
        {
            if (dstLo == _event)
            {
                _ = Mesh.SplitEdge(_pool, eUp.Sym!);
                Mesh.Splice(_pool, eLo.Sym!, eUp);
                regUp = TopLeftRegion(regUp);
                eUp = RegionBelow(regUp).EUp!;
                _ = FinishLeftRegions(RegionBelow(regUp), regLo);
                AddRightEdges(regUp, eUp.Oprev!, eUp, eUp, true);
                _pool.Return(ref isect);
                return true;
            }

            if (dstUp == _event)
            {
                _ = Mesh.SplitEdge(_pool, eLo.Sym!);
                Mesh.Splice(_pool, eUp.Lnext!, eLo.Oprev!);
                regLo = regUp;
                regUp = TopRightRegion(regUp);
                MeshUtils.Edge e = RegionBelow(regUp).EUp!.Rprev!;
                regLo.EUp = eLo.Oprev;
                eLo = FinishLeftRegions(regLo, null);
                AddRightEdges(regUp, eLo.Onext!, eUp.Rprev!, e, true);
                _pool.Return(ref isect);
                return true;
            }

            if (Geom.EdgeSign(dstUp, _event!, isect!) >= 0.0)
            {
                RegionAbove(regUp).Dirty = true;
                regUp.Dirty = true;
                _ = Mesh.SplitEdge(_pool, eUp.Sym!);
                eUp.Org!.S = _event!.S;
                eUp.Org.T = _event.T;
            }

            if (Geom.EdgeSign(dstLo, _event!, isect!) <= 0.0)
            {
                regUp.Dirty = true;
                regLo.Dirty = true;
                _ = Mesh.SplitEdge(_pool, eLo.Sym!);
                eLo.Org!.S = _event!.S;
                eLo.Org.T = _event.T;
            }

            _pool.Return(ref isect);
            return false;
        }

        _ = Mesh.SplitEdge(_pool, eUp.Sym!);
        _ = Mesh.SplitEdge(_pool, eLo.Sym!);
        Mesh.Splice(_pool, eLo.Oprev!, eUp);
        eUp.Org!.S = isect.S;
        eUp.Org.T = isect.T;
        _pool.Return(ref isect);
        eUp.Org.PqHandle = _pq!.Insert(eUp.Org);
        if (eUp.Org.PqHandle.Handle == PQHandle.Invalid)
        {
            throw new InvalidOperationException("PQHandle should not be invalid");
        }

        GetIntersectData(eUp.Org, orgUp, dstUp, orgLo, dstLo);
        RegionAbove(regUp).Dirty = true;
        regUp.Dirty = true;
        regLo.Dirty = true;
        return false;
    }

    private void WalkDirtyRegions(ActiveRegion regUp)
    {
        ActiveRegion regLo = RegionBelow(regUp);
        MeshUtils.Edge eUp, eLo;

        while (true)
        {
            while (regLo.Dirty)
            {
                regUp = regLo;
                regLo = RegionBelow(regLo);
            }

            if (!regUp.Dirty)
            {
                regLo = regUp;
                regUp = RegionAbove(regUp);
                if (regUp is null || !regUp.Dirty)
                {
                    return;
                }
            }

            regUp.Dirty = false;
            eUp = regUp.EUp!;
            eLo = regLo.EUp!;

            if (eUp.Dst != eLo.Dst)
            {
                if (CheckForLeftSplice(regUp))
                {
                    if (regLo.FixUpperEdge)
                    {
                        DeleteRegion(regLo);
                        Mesh.Delete(_pool, eLo);
                        regLo = RegionBelow(regUp);
                        eLo = regLo.EUp!;
                    }
                    else if (regUp.FixUpperEdge)
                    {
                        DeleteRegion(regUp);
                        Mesh.Delete(_pool, eUp);
                        regUp = RegionAbove(regLo);
                        eUp = regUp.EUp!;
                    }
                }
            }

            if (eUp.Org != eLo.Org)
            {
                if (eUp.Dst != eLo.Dst && !regUp.FixUpperEdge && !regLo.FixUpperEdge
                    && (eUp.Dst == _event || eLo.Dst == _event))
                {
                    if (CheckForIntersect(regUp))
                    {
                        return;
                    }
                }
                else
                {
                    _ = CheckForRightSplice(regUp);
                }
            }

            if (eUp.Org == eLo.Org && eUp.Dst == eLo.Dst)
            {
                Geom.AddWinding(eLo, eUp);
                DeleteRegion(regUp);
                Mesh.Delete(_pool, eUp);
                regUp = RegionAbove(regLo);
            }
        }
    }

    private void ConnectRightVertex(ActiveRegion regUp, MeshUtils.Edge eBottomLeft)
    {
        MeshUtils.Edge eTopLeft = eBottomLeft.Onext!;
        ActiveRegion regLo = RegionBelow(regUp);
        MeshUtils.Edge eUp = regUp.EUp!;
        MeshUtils.Edge eLo = regLo.EUp!;
        bool degenerate = false;

        if (eUp.Dst != eLo.Dst)
        {
            _ = CheckForIntersect(regUp);
        }

        if (Geom.VertEq(eUp.Org!, _event!))
        {
            Mesh.Splice(_pool, eTopLeft.Oprev!, eUp);
            regUp = TopLeftRegion(regUp);
            eTopLeft = RegionBelow(regUp).EUp!;
            _ = FinishLeftRegions(RegionBelow(regUp), regLo);
            degenerate = true;
        }

        if (Geom.VertEq(eLo.Org!, _event!))
        {
            Mesh.Splice(_pool, eBottomLeft, eLo.Oprev!);
            eBottomLeft = FinishLeftRegions(regLo, null);
            degenerate = true;
        }

        if (degenerate)
        {
            AddRightEdges(regUp, eBottomLeft.Onext!, eTopLeft, eTopLeft, true);
            return;
        }

        MeshUtils.Edge eNew = Geom.VertLeq(eLo.Org!, eUp.Org!) ? eLo.Oprev! : eUp;
        eNew = Mesh.Connect(_pool, eBottomLeft.Lprev!, eNew);

        AddRightEdges(regUp, eNew, eNew.Onext!, eNew.Onext!, false);
        eNew.Sym!.ActiveRegion!.FixUpperEdge = true;
        WalkDirtyRegions(regUp);
    }

    private void ConnectLeftDegenerate(ActiveRegion regUp, MeshUtils.Vertex vEvent)
    {
        MeshUtils.Edge e = regUp.EUp!;
        if (Geom.VertEq(e.Org!, vEvent))
        {
            throw new InvalidOperationException("Vertices should have been merged before");
        }

        if (!Geom.VertEq(e.Dst!, vEvent))
        {
            _ = Mesh.SplitEdge(_pool, e.Sym!);
            if (regUp.FixUpperEdge)
            {
                Mesh.Delete(_pool, e.Onext!);
                regUp.FixUpperEdge = false;
            }

            Mesh.Splice(_pool, vEvent.AnEdge!, e);
            SweepEvent(vEvent);
            return;
        }

        throw new InvalidOperationException("Vertices should have been merged before");
    }

    private void ConnectLeftVertex(MeshUtils.Vertex vEvent)
    {
        ActiveRegion tmp = _pool.Get<ActiveRegion>();

        tmp.EUp = vEvent.AnEdge!.Sym;
        ActiveRegion regUp = _dict!.Find(tmp).Key!;
        ActiveRegion? tmpReturn = tmp;
        _pool.Return(ref tmpReturn);
        ActiveRegion regLo = RegionBelow(regUp);
        if (regLo is null)
        {
            return; // coplanar input
        }

        MeshUtils.Edge eUp = regUp.EUp!;
        MeshUtils.Edge eLo = regLo.EUp!;

        if (Geom.EdgeSign(eUp.Dst!, vEvent, eUp.Org!) == 0.0)
        {
            ConnectLeftDegenerate(regUp, vEvent);
            return;
        }

        ActiveRegion reg = Geom.VertLeq(eLo.Dst!, eUp.Dst!) ? regUp : regLo;

        if (regUp.Inside || reg.FixUpperEdge)
        {
            MeshUtils.Edge eNew = reg == regUp
                ? Mesh.Connect(_pool, vEvent.AnEdge!.Sym!, eUp.Lnext!)
                : Mesh.Connect(_pool, eLo.Dnext!, vEvent.AnEdge!).Sym!;

            if (reg.FixUpperEdge)
            {
                FixUpperEdge(reg, eNew);
            }
            else
            {
                ComputeWinding(AddRegionBelow(regUp, eNew));
            }

            SweepEvent(vEvent);
        }
        else
        {
            AddRightEdges(regUp, vEvent.AnEdge!, vEvent.AnEdge!, null, true);
        }
    }

    private void SweepEvent(MeshUtils.Vertex vEvent)
    {
        _event = vEvent;

        MeshUtils.Edge e = vEvent.AnEdge!;
        while (e.ActiveRegion is null)
        {
            e = e.Onext!;
            if (e == vEvent.AnEdge)
            {
                ConnectLeftVertex(vEvent);
                return;
            }
        }

        ActiveRegion regUp = TopLeftRegion(e.ActiveRegion!);
        ActiveRegion reg = RegionBelow(regUp);
        MeshUtils.Edge eTopLeft = reg.EUp!;
        MeshUtils.Edge eBottomLeft = FinishLeftRegions(reg, null);

        if (eBottomLeft.Onext == eTopLeft)
        {
            ConnectRightVertex(regUp, eBottomLeft);
        }
        else
        {
            AddRightEdges(regUp, eBottomLeft.Onext!, eTopLeft, eTopLeft, true);
        }
    }

    private void AddSentinel(double smin, double smax, double t)
    {
        MeshUtils.Edge e = _mesh!.MakeEdge(_pool);
        e.Org!.S = smax;
        e.Org.T = t;
        e.Dst!.S = smin;
        e.Dst.T = t;
        _event = e.Dst;

        ActiveRegion reg = _pool.Get<ActiveRegion>();
        reg.EUp = e;
        reg.WindingNumber = 0;
        reg.Inside = false;
        reg.FixUpperEdge = false;
        reg.Sentinel = true;
        reg.Dirty = false;
        reg.NodeUp = _dict!.Insert(reg);
    }

    private void InitEdgeDict()
    {
        if (_dict is null)
        {
            _dict = new Dict<ActiveRegion>(_pool, EdgeLeq);
        }
        else
        {
            _dict.Init();
        }

        AddSentinel(-SentinelCoord, SentinelCoord, -SentinelCoord);
        AddSentinel(-SentinelCoord, SentinelCoord, +SentinelCoord);
    }

    private void DoneEdgeDict()
    {
        int fixedEdges = 0;

        ActiveRegion reg;
        while ((reg = _dict!.Min().Key!) is not null)
        {
            if (!reg.Sentinel)
            {
                System.Diagnostics.Debug.Assert(reg.FixUpperEdge);
                System.Diagnostics.Debug.Assert(++fixedEdges == 1);
            }

            System.Diagnostics.Debug.Assert(reg.WindingNumber == 0);
            DeleteRegion(reg);
        }

        System.Diagnostics.Debug.Assert(_dict.Empty);
        _dict.Reset();
    }

    private void RemoveDegenerateEdges()
    {
        MeshUtils.Edge eHead = _mesh!.EHead!;
        MeshUtils.Edge e, eNext, eLnext;

        for (e = eHead.Next!; e != eHead; e = eNext)
        {
            eNext = e.Next!;
            eLnext = e.Lnext!;

            if (Geom.VertEq(e.Org!, e.Dst!) && eLnext.Lnext != e)
            {
                SpliceMergeVertices(eLnext, e);
                Mesh.Delete(_pool, e);
                e = eLnext;
                eLnext = e.Lnext!;
            }

            if (eLnext.Lnext == e)
            {
                if (eLnext != e)
                {
                    if (eLnext == eNext || eLnext == eNext.Sym)
                    {
                        eNext = eNext.Next!;
                    }

                    Mesh.Delete(_pool, eLnext);
                }

                if (e == eNext || e == eNext.Sym)
                {
                    eNext = eNext.Next!;
                }

                Mesh.Delete(_pool, e);
            }
        }
    }

    private void InitPriorityQ()
    {
        MeshUtils.Vertex vHead = _mesh!.VHead!;
        MeshUtils.Vertex v;
        int vertexCount = 0;

        for (v = vHead.Next!; v != vHead; v = v.Next!)
        {
            vertexCount++;
        }

        vertexCount += 8;

        _pq ??= new PriorityQueue<MeshUtils.Vertex>(vertexCount, Geom.VertLeq);

        vHead = _mesh.VHead!;
        for (v = vHead.Next!; v != vHead; v = v.Next!)
        {
            v.PqHandle = _pq.Insert(v);
            if (v.PqHandle.Handle == PQHandle.Invalid)
            {
                throw new InvalidOperationException("PQHandle should not be invalid");
            }
        }

        _pq.Init();
    }

    private void DonePriorityQ()
    {
        System.Diagnostics.Debug.Assert(_pq!.Empty);
    }

    private void RemoveDegenerateFaces()
    {
        MeshUtils.Face f, fNext;
        for (f = _mesh!.FHead!.Next!; f != _mesh.FHead; f = fNext)
        {
            fNext = f.Next!;
            MeshUtils.Edge e = f.AnEdge!;
            System.Diagnostics.Debug.Assert(e.Lnext != e);

            if (e.Lnext!.Lnext == e)
            {
                Geom.AddWinding(e.Onext!, e);
                Mesh.Delete(_pool, e);
            }
        }
    }

    /// <summary>
    /// Computes the planar arrangement of the contours, subdividing it into monotone
    /// regions marked inside/outside per the winding rule.
    /// </summary>
    private void ComputeInterior()
    {
        RemoveDegenerateEdges();
        InitPriorityQ();
        RemoveDegenerateFaces();
        InitEdgeDict();

        MeshUtils.Vertex v, vNext;
        while ((v = _pq!.ExtractMin()) is not null)
        {
            while (true)
            {
                vNext = _pq.Minimum();
                if (vNext is null || !Geom.VertEq(vNext, v))
                {
                    break;
                }

                vNext = _pq.ExtractMin();
                SpliceMergeVertices(v.AnEdge!, vNext.AnEdge!);
            }

            SweepEvent(v);
        }

        DoneEdgeDict();
        DonePriorityQ();

        RemoveDegenerateFaces();
        _mesh!.Check();
    }
}
