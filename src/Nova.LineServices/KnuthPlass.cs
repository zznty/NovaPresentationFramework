namespace Nova.LineServices;

/// <summary>
/// Knuth-Plass optimal paragraph breaking ("Breaking Paragraphs into Lines",
/// Knuth &amp; Plass, 1981) over a measured paragraph model. The Lo entry
/// points use the FIRST break of the optimal sequence per call; per-line
/// demerits depend only on adjacent breaks (no cross-line flags in v1), so
/// re-running the DP from the next line start reproduces the global optimum.
/// </summary>
internal static class KnuthPlass
{
    private const int BadnessCap = 10000;

    /// <summary>Forced paragraph-end break penalty (TeX "infinite penalty").</summary>
    internal const int InfinityPenalty = -10000;

    /// <summary>A break opportunity: exclusive char offset, cumulative width,
    /// and the glue (trailing space) at the opportunity.</summary>
    internal readonly struct BreakNode
    {
        internal BreakNode(int offset, int cumulativeWidth, int glueWidth, int glueStretch, int glueShrink, int penalty)
        {
            Offset = offset;
            CumulativeWidth = cumulativeWidth;
            GlueWidth = glueWidth;
            GlueStretch = glueStretch;
            GlueShrink = glueShrink;
            Penalty = penalty;
        }

        /// <summary>Exclusive char offset of the line end this node represents.</summary>
        internal int Offset { get; }

        /// <summary>Accumulated advance width from the paragraph start.</summary>
        internal int CumulativeWidth { get; }

        /// <summary>Width of the trailing space (0 for hyphen/CJK breaks).</summary>
        internal int GlueWidth { get; }

        /// <summary>Glue stretchability (justified interword stretch; sentinel for ragged).</summary>
        internal int GlueStretch { get; }

        /// <summary>Glue shrinkability (0 for ragged text).</summary>
        internal int GlueShrink { get; }

        /// <summary>Break penalty (0 ordinary, 50 dash breaks, InfinityPenalty paragraph end).</summary>
        internal int Penalty { get; }
    }

    /// <summary>
    /// Computes the optimal break sequence: the exclusive char offsets of every
    /// line end INCLUDING the paragraph end, or an empty array when the
    /// paragraph has no feasible break (caller falls back to the greedy
    /// emergency line). <paramref name="nodes"/> must be ordered by offset and
    /// end with the forced paragraph-end node (penalty InfinityPenalty).
    /// </summary>
    internal static int[] ComputeBreaks(IReadOnlyList<BreakNode> nodes, int targetWidth)
    {
        int nodeCount = nodes.Count;
        if (nodeCount < 2)
        {
            return [];
        }

        int last = nodeCount - 1;

        // Four fitness classes: 0 very tight (r < -0.5), 1 tight (-0.5..0.5),
        // 2 loose (0.5..1), 3 very loose (r > 1). best[class, node] = minimum
        // total demerits to reach node with the node's line in that class;
        // prev[class, node] / prevClass[class, node] reconstruct the path.
        const int Classes = 4;
        long[][] best = new long[Classes][];
        int[][] prev = new int[Classes][];
        int[][] prevClass = new int[Classes][];
        for (int c = 0; c < Classes; c++)
        {
            best[c] = new long[nodeCount];
            prev[c] = new int[nodeCount];
            prevClass[c] = new int[nodeCount];
            Array.Fill(best[c], long.MaxValue);
            Array.Fill(prev[c], -1);
            Array.Fill(prevClass[c], -1);
            best[c][0] = 0;
        }

        for (int j = 1; j < nodeCount; j++)
        {
            BreakNode end = nodes[j];
            for (int i = 0; i < j; i++)
            {
                BreakNode start = nodes[i];
                int naturalWidth = end.CumulativeWidth - start.CumulativeWidth;
                int stretch = 0;
                int shrink = 0;
                for (int k = i + 1; k <= j; k++)
                {
                    stretch += nodes[k].GlueStretch;
                    shrink += nodes[k].GlueShrink;
                }

                double r = naturalWidth <= targetWidth
                    ? (stretch > 0
                        ? (double)(targetWidth - naturalWidth) / stretch
                        : double.PositiveInfinity)
                    : (shrink > 0
                        ? (double)(targetWidth - naturalWidth) / shrink
                        : double.NegativeInfinity);

                // A break is feasible when the line can be set within the column
                // (shrink covers the excess) or the end node forces it. The first
                // line may not swallow the whole paragraph as an overfull end line:
                // the caller re-runs the DP per line, so allowing it would let the
                // degenerate single-line solution win on the forced-end penalty.
                bool forced = end.Penalty == InfinityPenalty;
                if (r < -1.0 && (!forced || i == 0))
                {
                    continue;
                }

                int badness = r switch
                {
                    <= -1.0 => BadnessCap,
                    < 0 => Math.Min(BadnessCap, -(int)((100 * r * r * r) - 0.5)),
                    <= 1.0 => Math.Min(BadnessCap, (int)((100 * r * r * r) + 0.5)),
                    _ => BadnessCap
                };

                int fitness = r switch
                {
                    < -0.5 => 0,
                    <= 0.5 => 1,
                    <= 1.0 => 2,
                    _ => 3
                };

                long demerits = badness + end.Penalty;
                demerits *= demerits;

                for (int ci = 0; ci < Classes; ci++)
                {
                    if (best[ci][i] == long.MaxValue)
                    {
                        continue;
                    }

                    // A line cannot be more than one class tighter than its predecessor.
                    if (fitness < ci - 1)
                    {
                        continue;
                    }

                    long candidate = best[ci][i] + demerits;
                    if (candidate < best[fitness][j])
                    {
                        best[fitness][j] = candidate;
                        prev[fitness][j] = i;
                        prevClass[fitness][j] = ci;
                    }
                }
            }
        }

        // Backtrack from the best final state.
        int bestClass = 0;
        for (int c = 1; c < Classes; c++)
        {
            if (best[c][last] < best[bestClass][last])
            {
                bestClass = c;
            }
        }

        if (best[bestClass][last] == long.MaxValue)
        {
            return [];
        }

        for (int c = 0; c < Classes; c++)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                if (best[c][i] != long.MaxValue)
                {
                }
            }
        }

        var offsets = new List<int>();
        int node = last;
        int cls = bestClass;
        while (node > 0)
        {
            offsets.Add(nodes[node].Offset);
            int source = prev[cls][node];
            int sourceClass = prevClass[cls][node];
            if (source <= 0 || sourceClass < 0)
            {
                break;
            }

            node = source;
            cls = sourceClass;
        }

        offsets.Reverse();
        return [.. offsets];
    }
}
