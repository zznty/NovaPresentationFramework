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
/// Sorted dictionary used by the sweep to keep active regions in edge order. A threaded
/// circular list with an insert/search that maintains the ordering via a LessOrEqual
/// delegate — allocation-light and stable for the sweep's access pattern.
/// </summary>
internal sealed class Dict<TValue> where TValue : class
{
    internal sealed class Node : IPooled<Node>
    {
        internal TValue? Key;
        internal Node? Prev;
        internal Node? Next;

        public void Init(TessPool pool)
        {
            Key = null;
            Prev = null;
            Next = null;
        }

        public void Reset(TessPool pool)
        {
            Key = null;
            Prev = null;
            Next = null;
        }
    }

    internal delegate bool LessOrEqual(TValue lhs, TValue rhs);

    private readonly TessPool _pool;
    private readonly LessOrEqual _leq;
    private Node? _head;

    public bool Empty => _head!.Next == _head;

    public Dict(TessPool pool, LessOrEqual leq)
    {
        _pool = pool;
        _leq = leq;
        Init();
    }

    public void Init()
    {
        _head = _pool.Get<Node>();
        _head.Prev = _head;
        _head.Next = _head;
    }

    public void Reset()
    {
        _pool.Return(ref _head);
    }

    public Node Insert(TValue key)
    {
        return InsertBefore(_head!, key);
    }

    public Node InsertBefore(Node node, TValue key)
    {
        do
        {
            node = node.Prev!;
        }
        while (node.Key is not null && !_leq(node.Key, key));

        Node newNode = _pool.Get<Node>();
        newNode.Key = key;
        newNode.Next = node.Next;
        node.Next!.Prev = newNode;
        newNode.Prev = node;
        node.Next = newNode;

        return newNode;
    }

    public Node Find(TValue key)
    {
        Node node = _head!;
        do
        {
            node = node.Next!;
        }
        while (node.Key is not null && !_leq(key, node.Key));
        return node;
    }

    public Node Min()
    {
        return _head!.Next!;
    }

    public void Remove(Node node)
    {
        node.Next!.Prev = node.Prev;
        node.Prev!.Next = node.Next;
        Node? nullable = node;
        _pool.Return(ref nullable);
    }
}
