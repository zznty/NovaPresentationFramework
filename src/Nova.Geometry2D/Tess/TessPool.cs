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

/// <summary>
/// Reusable-node contract for mesh elements. Nodes live on per-type free lists in
/// <see cref="TessPool"/> so repeated tessellations (e.g. per-frame stroke widening)
/// do not allocate.
/// </summary>
internal interface IPooled<T> where T : class, IPooled<T>, new()
{
    public void Init(TessPool pool);

    public void Reset(TessPool pool);
}

/// <summary>
/// Per-instance node pool. The sweep creates and destroys a large number of
/// vertices/edges/faces; free lists recycle them. Single-threaded by design (a
/// tessellation is one call).
/// </summary>
internal sealed class TessPool
{
    private static readonly Type[] PooledTypes =
    [
        typeof(Mesh),
        typeof(MeshUtils.Vertex),
        typeof(MeshUtils.Face),
        typeof(MeshUtils.Edge),
        typeof(Tess.ActiveRegion),
        typeof(Dict<Tess.ActiveRegion>.Node),
    ];

    private readonly Dictionary<Type, Stack<object>> _pools = [];

    public TessPool()
    {
        foreach (Type type in PooledTypes)
        {
            _pools[type] = new Stack<object>();
        }
    }

    public T Get<T>() where T : class, IPooled<T>, new()
    {
        Stack<object> pool = _pools[typeof(T)];
        T? node = pool.Count > 0 ? (T)pool.Pop() : null;
        node ??= new T();
        node.Init(this);
        return node;
    }

    public void Return<T>(ref T? node) where T : class, IPooled<T>, new()
    {
        if (node is null)
        {
            return;
        }

        node.Reset(this);
        _pools[typeof(T)].Push(node);
        node = null;
    }
}
