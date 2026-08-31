using System.Collections.Concurrent;
using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// Process-wide table mapping the opaque <see cref="nint"/> "WIC handles" that the patched
/// WPF nest passes across the managed boundary to the managed objects backing them. On this
/// host there is no windowscodecs COM: every <c>IWIC*</c> pointer is a token into this table,
/// exactly like the <c>nint</c> channel handles in <c>Nova.Mil.DuceExports</c>.
/// </summary>
/// <remarks>
/// Handle values are allocated from one process-wide space and never reused while live, so a
/// stale pointer cannot alias a newer object (the same invariant the DUCE channel handle table
/// relies on). Release of an unknown token is a no-op, which makes double-release safe when a
/// SafeHandle and an ownership transfer both touch the same token.
/// </remarks>
[PublicAPI]
public static class WicHandleTable
{
    private sealed record Entry(int RefCount, object Value);

    private static readonly ConcurrentDictionary<nint, Entry> s_entries = new();
    private static long s_nextHandle;

    /// <summary>Registers <paramref name="value"/> and returns a fresh non-zero token.</summary>
    public static nint Create<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        nint token;
        do
        {
            token = (nint)Interlocked.Increment(ref s_nextHandle);
        }
        while (token == 0 || !s_entries.TryAdd(token, new Entry(1, value)));

        return token;
    }

    /// <summary>Returns the object for <paramref name="token"/>, or <c>null</c> when unknown.</summary>
    public static T? TryGet<T>(nint token)
        where T : class
    {
        return token != 0 && s_entries.TryGetValue(token, out Entry? entry)
            ? entry.Value as T
            : null;
    }

    /// <summary>Bumps the reference count so a second SafeHandle can share one token.</summary>
    public static void AddRef(nint token)
    {
        if (token == 0)
        {
            return;
        }

        while (s_entries.TryGetValue(token, out Entry? entry))
        {
            if (s_entries.TryUpdate(token, entry with { RefCount = entry.RefCount + 1 }, entry))
            {
                return;
            }
        }
    }

    /// <summary>Drops one reference; removes (and disposes, when IDisposable) at zero. Unknown
    /// tokens are no-ops, which makes double-release safe after an ownership transfer.</summary>
    public static void Release(nint token)
    {
        if (token == 0)
        {
            return;
        }

        while (s_entries.TryGetValue(token, out Entry? entry))
        {
            if (entry.RefCount <= 1)
            {
                if (s_entries.TryRemove(token, out Entry? removed) && removed.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                return;
            }

            if (s_entries.TryUpdate(token, entry with { RefCount = entry.RefCount - 1 }, entry))
            {
                return;
            }
        }
    }

    /// <summary>Entry count (diagnostics/tests).</summary>
    public static int Count => s_entries.Count;
}
