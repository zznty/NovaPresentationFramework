using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace Nova.SystemTheme;

/// <summary>
/// Managed replacement for kernel32 <c>WaitForMultipleObjectsEx</c> (bAlertable: false),
/// which WindowsBase's <c>NonPumpingSynchronizationContext</c> P/Invokes and which cannot
/// resolve on Linux. Semantics mirror the Windows call exactly: block until the handles are
/// signaled or the timeout elapses, never pump a message pipe (the call site is the
/// non-pumping context by design). If a future host needs message-pipe pumping during a
/// wait, add a pump hook here — do not hand-roll waits at the call sites.
/// </summary>
[PublicAPI]
public static class WaitHandles
{
    /// <summary>Maximum handles, matching the Win32 <c>MAXIMUM_WAIT_OBJECTS</c> limit.</summary>
    public const int MaxHandles = 64;

    /// <summary>
    /// Waits on raw OS wait handles the caller owns (never closed here).
    /// Returns the signaled index (WaitAny semantics) or 0 (WaitAll semantics) on success,
    /// <see cref="WaitHandle.WaitTimeout"/> (0x102) on timeout — the Win32 result values.
    /// </summary>
    public static int WaitMultiple(IntPtr[] handles, bool waitAll, int millisecondsTimeout)
    {
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Length > MaxHandles)
        {
            throw new NotSupportedException($"{nameof(WaitMultiple)} supports at most {MaxHandles} handles.");
        }

        if (handles.Length == 0)
        {
            return WaitHandle.WaitTimeout;
        }

        var wrappers = new RawWaitHandle[handles.Length];
        try
        {
            for (int i = 0; i < handles.Length; i++)
            {
                wrappers[i] = new RawWaitHandle(handles[i]);
            }

            // NOTE: never call WaitHandle.WaitAll/WaitAny here — their per-handle WaitOne
            // path goes through WaitOneNoCheck which can re-enter this same WaitMultiple
            // (the WindowsBase Wait replacement), causing unbounded recursion. A
            // ZERO-timeout probe is terminal (returns immediately, no wait machinery
            // recursion); this loop owns the blocking.
            var deadline = millisecondsTimeout < 0 ? -1 : Environment.TickCount + millisecondsTimeout;
            bool[] signaled = new bool[handles.Length];
            int signaledCount = 0;
            while (true)
            {
                for (int i = 0; i < wrappers.Length; i++)
                {
                    if (!signaled[i] && wrappers[i].WaitOne(0))
                    {
                        signaled[i] = true;
                        signaledCount++;
                        if (!waitAll)
                        {
                            return i;
                        }
                    }
                }

                if (waitAll && signaledCount == wrappers.Length)
                {
                    return 0;
                }

                if (deadline >= 0 && Environment.TickCount >= deadline)
                {
                    return WaitHandle.WaitTimeout;
                }

                Thread.Sleep(1);
            }
        }
        finally
        {
            foreach (var wrapper in wrappers)
            {
                wrapper?.Dispose();
            }
        }
    }
}

/// <summary>
/// <see cref="WaitHandle"/> over a raw OS handle the caller owns; disposal never closes the
/// underlying handle (SafeWaitHandle ownsHandle: false).
/// </summary>
[PublicAPI]
public sealed class RawWaitHandle : WaitHandle
{
    public RawWaitHandle(IntPtr handle)
    {
        SafeWaitHandle = new SafeWaitHandle(handle, false);
    }
}
