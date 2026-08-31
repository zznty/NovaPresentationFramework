using JetBrains.Annotations;

namespace Nova.Mil;

/// <summary>
/// Process-wide hook that binds the managed DUCE transport to the <see cref="SlaveGraph"/>s
/// and present callbacks of the live composition frames. WPF uses ONE channel set (main +
/// out-of-band) per MediaContext for every composition target on it, so a channel set owns a
/// single shared <see cref="SlaveGraph"/> and the composition targets on it are multiplexed by
/// their target resource handle. Each frame registers its own present callback; <see cref="Present"/>
/// renders every attached frame. No WPF types.
/// </summary>
[PublicAPI]
public static class DuceRuntime
{
    private sealed record Binding(int Id, SlaveGraph Graph, Action Present);

    private static readonly Lock s_gate = new();
    private static readonly List<Binding> s_bindings = [];
    private static readonly Dictionary<nint, SlaveGraph> s_graphsByChannel = [];
    private static int s_nextBindingId;

    /// <summary>
    /// Registers a frame's present callback. Each frame calls this once from its composition
    /// target; <see cref="Present"/> then renders every attached frame. Returns the binding id
    /// used by <see cref="Detach(int)"/>.
    /// </summary>
    public static int Attach(SlaveGraph graph, Action present)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(present);
        lock (s_gate)
        {
            int id = s_nextBindingId++;
            s_bindings.Add(new Binding(id, graph, present));
            return id;
        }
    }

    /// <summary>Removes the most recently attached binding. Convenience for the single-frame
    /// case; multi-frame hosts use <see cref="Detach(int)"/>.</summary>
    public static void Detach()
    {
        lock (s_gate)
        {
            if (s_bindings.Count > 0)
            {
                Binding removed = s_bindings[^1];
                s_bindings.RemoveAt(s_bindings.Count - 1);
                RemoveChannelMappingsIfUnused(removed.Graph);
            }
        }
    }

    /// <summary>Removes the binding with the given id. A no-op for unknown ids.</summary>
    public static void Detach(int bindingId)
    {
        lock (s_gate)
        {
            for (int i = s_bindings.Count - 1; i >= 0; i--)
            {
                if (s_bindings[i].Id == bindingId)
                {
                    Binding removed = s_bindings[i];
                    s_bindings.RemoveAt(i);
                    RemoveChannelMappingsIfUnused(removed.Graph);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Removes a channel set's mappings when no live frame still presents that graph, so a
    /// closed window's channel mappings do not linger (and the sole-binding fallback is not
    /// poisoned by a stale entry). A graph with no live frames has no registered targets, so
    /// no commits can arrive for it.
    /// </summary>
    private static void RemoveChannelMappingsIfUnused(SlaveGraph graph)
    {
        bool inUse = s_bindings.Exists(binding => ReferenceEquals(binding.Graph, graph));
        if (inUse)
        {
            return;
        }

        foreach (KeyValuePair<nint, SlaveGraph> pair in s_graphsByChannel)
        {
            if (ReferenceEquals(pair.Value, graph))
            {
                _ = s_graphsByChannel.Remove(pair.Key);
            }
        }
    }

    /// <summary>
    /// Associates a channel set (main + out-of-band channel handles of one MediaContext) with
    /// a shared slave graph. Returns the existing graph when the set is already registered, or
    /// seeds it with <paramref name="seedGraph"/> (the first frame's graph) and returns it.
    /// All commands on either channel are parsed into this graph; targets on it are
    /// multiplexed by their target resource handle.
    /// </summary>
    public static SlaveGraph GetOrCreateChannelGraph(nint channelHandle, nint outOfBandChannelHandle, SlaveGraph seedGraph)
    {
        ArgumentNullException.ThrowIfNull(seedGraph);
        lock (s_gate)
        {
            if (s_graphsByChannel.TryGetValue(channelHandle, out SlaveGraph? existing))
            {
                return existing;
            }

            s_graphsByChannel[channelHandle] = seedGraph;
            s_graphsByChannel[outOfBandChannelHandle] = seedGraph;
            return seedGraph;
        }
    }

    /// <summary>
    /// The graph that owns the given channel, or <c>null</c>. Channels that never registered a
    /// channel set (the service channel, unit-test channels) fall back to the sole attached
    /// frame so the single-window path keeps working.
    /// </summary>
    internal static SlaveGraph? GraphFor(nint channelHandle)
    {
        lock (s_gate)
        {
            return s_graphsByChannel.TryGetValue(channelHandle, out SlaveGraph? graph)
                ? graph
                : s_bindings.Count == 1
                    ? s_bindings[0].Graph
                    : null;
        }
    }

    /// <summary>
    /// Invokes the present callback of every attached frame. The host/app loop drives the
    /// composition pass by calling this (or <c>SdlPresentationSource.Present</c>, which
    /// delegates here) once per iteration so that popup/tooltip frames registered later on
    /// the shared channel set are rendered alongside the main window — a loop that presents
    /// only the main source's frame leaves popup windows unrendered.
    /// </summary>
    public static void Present()
    {
        _ = Interlocked.Increment(ref s_presentCount);

        Binding[] snapshot;
        lock (s_gate)
        {
            snapshot = [.. s_bindings];
        }

        foreach (Binding binding in snapshot)
        {
            binding.Present();
        }
    }

    private static long s_presentCount;

    /// <summary>Total number of <see cref="Present"/> calls since process start (diagnostic).</summary>
    public static long PresentCount => Volatile.Read(ref s_presentCount);
}
