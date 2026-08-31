using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;
using Nova.MilCmd;

namespace Nova.Mil;

/// <summary>
/// Managed replacements for MilCore nests. Handles are blittable <see cref="uint"/>.
/// The patched <c>[DllImport]</c> nests in the WPF submodule call into this type
/// (patches/0001, via <c>using Nova.Mil;</c>).
/// </summary>
[PublicAPI]
public static class DuceExports
{
    public const int SOk = 0;
    public const int EFail = unchecked((int)0x80004005);
    public const int EInvalidArg = unchecked((int)0x80070057);
    public const int ENotImplemented = unchecked((int)0x80004001);
    public const uint ExpectedMilSdkVersion = 0x200184C0;
    public const uint WaitInfinite = uint.MaxValue;
    public const int MarshalTypeSameThread = 1;

    public const int MessageSyncFlushReply = 0x01;
    public const int MessageCaps = 0x04;
    public const int MessagePresented = 0x0A;

    private static readonly Lock s_compositionLock = new();
    private static readonly Lock s_mediaLock = new();
    private static readonly ConcurrentDictionary<nint, DuceConnection> s_connections = new();
    private static readonly ConcurrentDictionary<nint, DuceChannel> s_channels = new();
    private static int s_nextPointer = 1;
    private static long s_nextPerfId;
    private static int s_loggedNotImplemented;

    public static int VersionCheck(uint sdkVersion)
    {
        return sdkVersion == ExpectedMilSdkVersion ? SOk : EFail;
    }

    public static void EnterCompositionLock()
    {
        s_compositionLock.Enter();
    }

    public static void ExitCompositionLock()
    {
        s_compositionLock.Exit();
    }

    public static void EnterMediaLock()
    {
        s_mediaLock.Enter();
    }

    public static void ExitMediaLock()
    {
        s_mediaLock.Exit();
    }

    public static int InitializePartitionManager(int priority)
    {
        _ = priority;
        return SOk;
    }

    public static int DeinitializePartitionManager()
    {
        return SOk;
    }

    public static long NextPerfElementId()
    {
        return Interlocked.Increment(ref s_nextPerfId);
    }

    public static bool ShouldForceSoftware()
    {
        return false;
    }

    public static int CreateConnection(bool requestSynchronousTransport, out nint connection)
    {
        _ = requestSynchronousTransport;
        nint pointer = NextPointer();
        s_connections[pointer] = new DuceConnection();
        connection = pointer;
        return SOk;
    }

    public static int DisconnectConnection(nint connection)
    {
        if (connection == 0)
        {
            return SOk;
        }

        _ = s_connections.TryRemove(connection, out _);
        return SOk;
    }

    public static int Present(nint connection)
    {
        if (!s_connections.ContainsKey(connection))
        {
            return EInvalidArg;
        }

        DuceRuntime.Present();
        foreach (DuceChannel channel in s_channels.Values)
        {
            channel.EnqueuePresented();
        }

        return SOk;
    }

    public static int CreateChannel(nint transport, nint referenceChannel, out nint channel)
    {
        if (transport != 0 && !s_connections.ContainsKey(transport))
        {
            channel = 0;
            return EInvalidArg;
        }

        _ = referenceChannel;
        nint pointer = NextPointer();
        s_channels[pointer] = new DuceChannel(pointer);
        channel = pointer;
        return SOk;
    }

    public static int DestroyChannel(nint channel)
    {
        if (channel == 0)
        {
            return SOk;
        }

        _ = s_channels.TryRemove(channel, out _);
        return SOk;
    }

    public static int CloseBatch(nint channel)
    {
        return TryGetChannel(channel, out _) ? SOk : EInvalidArg;
    }

    public static int CommitChannel(nint channel)
    {
        return TryGetChannel(channel, out DuceChannel? found) ? found.Commit() : EInvalidArg;
    }

    public static int GetMarshalType(nint channel, out int marshalType)
    {
        if (!TryGetChannel(channel, out _))
        {
            marshalType = 0;
            return EInvalidArg;
        }

        marshalType = MarshalTypeSameThread;
        return SOk;
    }

    public static int CreateOrAddRef(nint channel, uint resourceType, ref uint handle)
    {
        return TryGetChannel(channel, out DuceChannel? found)
            ? found.CreateOrAddRef(resourceType, ref handle)
            : EInvalidArg;
    }

    public static int Release(nint channel, uint handle, out int deleted)
    {
        if (!TryGetChannel(channel, out DuceChannel? found))
        {
            deleted = 0;
            return EInvalidArg;
        }

        return found.Release(handle, out deleted);
    }

    public static int GetRefCount(nint channel, uint handle, out uint refCount)
    {
        if (!TryGetChannel(channel, out DuceChannel? found))
        {
            refCount = 0;
            return EInvalidArg;
        }

        return found.GetRefCount(handle, out refCount);
    }

    public static unsafe int SendCommand(byte* data, uint size, bool sendInSeparateBatch, nint channel)
    {
        return TryGetChannel(channel, out DuceChannel? found) && data is not null
            ? found.SendCommand(new ReadOnlySpan<byte>(data, (int)size), sendInSeparateBatch)
            : EInvalidArg;
    }

    public static unsafe int BeginCommand(nint channel, byte* data, uint size, uint extra)
    {
        return TryGetChannel(channel, out DuceChannel? found) && data is not null
            ? found.BeginCommand(new ReadOnlySpan<byte>(data, (int)size), extra)
            : EInvalidArg;
    }

    public static unsafe int AppendCommandData(nint channel, byte* data, uint size)
    {
        return TryGetChannel(channel, out DuceChannel? found) && data is not null
            ? found.AppendCommandData(new ReadOnlySpan<byte>(data, (int)size))
            : EInvalidArg;
    }

    public static int EndCommand(nint channel)
    {
        return TryGetChannel(channel, out DuceChannel? found) ? found.EndCommand() : EInvalidArg;
    }

    public static int SyncFlush(nint channel)
    {
        if (!TryGetChannel(channel, out DuceChannel? found))
        {
            return EInvalidArg;
        }

        int hr = found.Commit();
        if (hr != SOk)
        {
            return hr;
        }

        found.EnqueueSyncFlushReply();
        return SOk;
    }

    public static unsafe int PeekNextMessage(nint channel, void* message, nuint messageSize, out int retrieved)
    {
        retrieved = 0;
        if (!TryGetChannel(channel, out DuceChannel? found) || message is null)
        {
            return EInvalidArg;
        }

        if (!found.TryDequeueMessage(out DuceMessage queued))
        {
            return SOk;
        }

        int copy = (int)Math.Min(messageSize, (nuint)Marshal.SizeOf<DuceMessage>());
        new ReadOnlySpan<byte>(&queued, copy).CopyTo(new Span<byte>(message, copy));
        retrieved = 1;
        return SOk;
    }

    public static int WaitForNextMessage(
        nint channel,
        int count,
        IntPtr[]? handles,
        int waitAll,
        uint timeout,
        out int waitReturn)
    {
        _ = count;
        _ = handles;
        _ = waitAll;
        waitReturn = 0;
        if (!TryGetChannel(channel, out DuceChannel? found))
        {
            return EInvalidArg;
        }

        if (found.HasMessage || timeout != 0)
        {
            return SOk;
        }

        waitReturn = 258;
        return SOk;
    }

    public static int SetNotificationWindow(nint channel, nint hwnd, int message)
    {
        _ = hwnd;
        _ = message;
        return TryGetChannel(channel, out _) ? SOk : EInvalidArg;
    }

    public static int DuplicateHandle(nint sourceChannel, uint original, nint targetChannel, ref uint duplicate)
    {
        if (!TryGetChannel(sourceChannel, out DuceChannel? source)
            || !TryGetChannel(targetChannel, out DuceChannel? target))
        {
            duplicate = 0;
            return EInvalidArg;
        }

        if (original == 0 || !source.Contains(original))
        {
            duplicate = 0;
            return EInvalidArg;
        }

        if (!source.TryGetType(original, out MilResourceType type))
        {
            duplicate = 0;
            return EInvalidArg;
        }

        return target.DuplicateFrom(original, type, ref duplicate);
    }

    public static int SendCommandMedia(uint handle, nint media, nint channel, bool notifyUceDirect)
    {
        _ = handle;
        _ = media;
        _ = channel;
        _ = notifyUceDirect;
        return LogNotImplemented();
    }

    public static int SendCommandBitmapSource(uint handle, nint bitmapSource, nint channel)
    {
        if (bitmapSource == 0)
        {
            return EInvalidArg;
        }

        SlaveGraph? graph = DuceRuntime.GraphFor(channel);
        if (graph is null)
        {
            return SOk;
        }

        // WPF caches decoded frames per URI, so two BitmapImages over the same image share ONE
        // frame handle: the token must stay in the handle table (refcount releases dispose it)
        // and the graph slot gets its own owning copy. Detaching here broke the second consumer
        // — its WicSourceHandle became a dead token and its bitmap never reached the graph.
        Nova.Imaging.ManagedWicBitmap? bitmap = Nova.Imaging.WicHandleTable.TryGet<Nova.Imaging.ManagedWicBitmap>(bitmapSource)
            ?? (Nova.Imaging.WicHandleTable.TryGet<Nova.Imaging.ManagedFormatConverter>(bitmapSource) is { Source: not null } converter ? converter.Source : null);
        if (bitmap is null)
        {
            return SOk;
        }

        graph.SetBitmapSourcePixels(new ResourceHandle(handle), bitmap.Clone());
        return SOk;
    }

    private static int LogNotImplemented()
    {
        if (Interlocked.Exchange(ref s_loggedNotImplemented, 1) == 0)
        {
            Console.Error.WriteLine("DuceExports: E_NOTIMPL");
        }

        return ENotImplemented;
    }

    private static nint NextPointer()
    {
        return Interlocked.Increment(ref s_nextPointer);
    }

    private static bool TryGetChannel(nint handle, [NotNullWhen(true)] out DuceChannel? channel)
    {
        if (handle != 0 && s_channels.TryGetValue(handle, out channel))
        {
            return true;
        }

        channel = null;
        return false;
    }

    private sealed class DuceConnection
    {
    }

    private sealed class DuceChannel(nint channelHandle)
    {
        // Handle values are allocated from ONE process-wide space, not per channel. All
        // channels (main, out-of-band, service) feed the SAME shared SlaveGraph, which keys
        // resources by handle VALUE only; per-channel counters let a second target's
        // out-of-band content root take a value already owned by the first target's content
        // tree (e.g. value 2), and releasing it then deleted the live resource of another
        // window — the black main window behind a popup. Mirrors milcore, where resource
        // handles are unique per partition.
        private static uint s_nextHandle = 1;

        private readonly Dictionary<uint, uint> _refCounts = [];
        private readonly Dictionary<uint, MilResourceType> _resourceTypes = [];
        private readonly List<byte> _batch = [];
        private readonly Queue<DuceMessage> _messages = new();
        private readonly List<byte> _openCommand = [];
        private bool _commandOpen;
        private bool _sentCaps;

        public bool HasMessage
        {
            get
            {
                EnsureCaps();
                return _messages.Count > 0;
            }
        }

        public bool Contains(uint handle)
        {
            return _refCounts.ContainsKey(handle);
        }

        public bool TryGetType(uint handle, out MilResourceType type)
        {
            return _resourceTypes.TryGetValue(handle, out type);
        }

        public int DuplicateFrom(uint original, MilResourceType type, ref uint duplicate)
        {
            // WPF's MultiChannelResource shares one handle VALUE across channels (the
            // content-root's out-of-band handle is "duplicated" onto the shared main
            // channel). Allocate a fresh handle from the shared handle space so the
            // duplicate never collides with another channel's resource, and record the
            // source resource's type so the shared graph creates a Visual slot for it;
            // the graph keys each target's root by this handle.
            _ = original;
            duplicate = AllocateHandle();
            _refCounts[duplicate] = 1;
            _resourceTypes[duplicate] = type;
            DuceRuntime.GraphFor(channelHandle)?.VisitChannelCreateResource(new ResourceHandle(duplicate), type);
            return SOk;
        }

        public int CreateOrAddRef(uint resourceType, ref uint handle)
        {
            if (handle == 0)
            {
                handle = AllocateHandle();
                _refCounts[handle] = 1;
                _resourceTypes[handle] = (MilResourceType)resourceType;
                DuceRuntime.GraphFor(channelHandle)?.VisitChannelCreateResource(new ResourceHandle(handle), (MilResourceType)resourceType);
                return SOk;
            }

            if (!_refCounts.TryGetValue(handle, out uint count))
            {
                _refCounts[handle] = 1;
                _resourceTypes[handle] = (MilResourceType)resourceType;
                return SOk;
            }

            _refCounts[handle] = count + 1;
            return SOk;
        }

        public int Release(uint handle, out int deleted)
        {
            deleted = 0;
            if (!_refCounts.TryGetValue(handle, out uint count))
            {
                return EInvalidArg;
            }

            if (count > 1)
            {
                _refCounts[handle] = count - 1;
                return SOk;
            }

            _ = _refCounts.Remove(handle);
            _ = _resourceTypes.Remove(handle);
            deleted = 1;
            DuceRuntime.GraphFor(channelHandle)?.VisitChannelDeleteResource(new ResourceHandle(handle));
            return SOk;
        }

        public int GetRefCount(uint handle, out uint refCount)
        {
            if (!_refCounts.TryGetValue(handle, out refCount))
            {
                refCount = 0;
                return EInvalidArg;
            }

            return SOk;
        }

        private static uint AllocateHandle()
        {
            return Interlocked.Increment(ref s_nextHandle);
        }

        public int SendCommand(ReadOnlySpan<byte> data, bool sendInSeparateBatch)
        {
            if (sendInSeparateBatch)
            {
                Parse(data);
                return SOk;
            }

            _batch.AddRange(data);
            return SOk;
        }

        public int BeginCommand(ReadOnlySpan<byte> data, uint extra)
        {
            _ = extra;
            if (_commandOpen)
            {
                return EFail;
            }

            _openCommand.Clear();
            _openCommand.AddRange(data);
            _commandOpen = true;
            return SOk;
        }

        public int AppendCommandData(ReadOnlySpan<byte> data)
        {
            if (!_commandOpen)
            {
                return EFail;
            }

            _openCommand.AddRange(data);
            return SOk;
        }

        public int EndCommand()
        {
            if (!_commandOpen)
            {
                return EFail;
            }

            _batch.AddRange(_openCommand);
            _openCommand.Clear();
            _commandOpen = false;
            return SOk;
        }

        public int Commit()
        {
            if (_batch.Count == 0)
            {
                return SOk;
            }

            byte[] records = [.. _batch];
            _batch.Clear();
            Parse(records);
            return SOk;
        }

        public void EnqueuePresented()
        {
            EnsureCaps();
            _messages.Enqueue(new DuceMessage(MessagePresented, refreshRate: 60));
        }

        public void EnqueueSyncFlushReply()
        {
            EnsureCaps();
            _messages.Enqueue(new DuceMessage(MessageSyncFlushReply));
        }

        public bool TryDequeueMessage(out DuceMessage message)
        {
            EnsureCaps();
            return _messages.TryDequeue(out message);
        }

        private void EnsureCaps()
        {
            if (_sentCaps)
            {
                return;
            }

            _sentCaps = true;
            _messages.Enqueue(new DuceMessage(MessageCaps));
        }

        private void Parse(ReadOnlySpan<byte> records)
        {
            SlaveGraph? graph = DuceRuntime.GraphFor(channelHandle);
            if (graph is null || records.IsEmpty)
            {
                return;
            }

            MilCommandParser.ParseChannel(records, graph);
        }
    }
}
