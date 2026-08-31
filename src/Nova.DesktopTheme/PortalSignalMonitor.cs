using System.Buffers.Binary;
using System.Net.Sockets;

namespace Nova.DesktopTheme;

/// <summary>
/// Listens for the xdg-desktop-portal <c>SettingChanged</c> signal on the session bus and
/// raises <see cref="AppearanceChanged"/> when the <c>org.freedesktop.appearance</c> namespace
/// changes (dark/light or accent). This is the cross-desktop live-restyle signal; KDE's own
/// <c>org.kde.KGlobalSettings</c> interface is dead on this box (ServiceUnknown, measured).
/// The reader thread is a single blocking socket receive; <see cref="Dispose"/> unblocks it
/// by closing the socket. Any failure silently stops the listener (live restyle then relies
/// on the file watcher).
/// </summary>
public sealed class PortalSignalMonitor : IDisposable
{
    private readonly Socket? _socket;
    private readonly Thread? _thread;
    private readonly CancellationTokenSource _cancellation = new();
    private int _disposed;

    public PortalSignalMonitor()
    {
        _socket = TryConnect();
        if (_socket is null)
        {
            return;
        }

        _thread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "nova-portal-signal"
        };
        _thread.Start();
    }

    /// <summary>Raised when the portal reports an appearance setting change.</summary>
    public event EventHandler? AppearanceChanged;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket?.Dispose();
        _ = _thread?.Join(TimeSpan.FromSeconds(1));
        _cancellation.Dispose();
    }

    private void ListenLoop()
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (_disposed == 0)
            {
                int n = _socket!.Receive(buffer);
                if (n <= 0)
                {
                    return;
                }

                int offset = 0;
                while (offset + 16 <= n)
                {
                    if (buffer[offset] != 0x6c)
                    {
                        return;
                    }

                    if (buffer[offset + 1] != DbusWire.MessageTypeSignal)
                    {
                        offset = SkipMessage(buffer, n, offset);
                        continue;
                    }

                    uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 4, 4));
                    uint fieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 12, 4));
                    int messageLength = 16 + (int)fieldsLength + (int)bodyLength;
                    if (offset + messageLength > n)
                    {
                        return; // incomplete message: drop (we only need the first signal anyway)
                    }

                    if (IsAppearanceChanged(buffer.AsSpan(offset + 16, (int)fieldsLength)))
                    {
                        AppearanceChanged?.Invoke(this, EventArgs.Empty);
                        return; // one-shot: the next palette apply restarts the monitor
                    }

                    offset += messageLength;
                }
            }
        }
        catch (SocketException)
        {
        }
        catch (DbusProtocolException)
        {
        }
    }

    private static int SkipMessage(byte[] buffer, int length, int offset)
    {
        if (offset + 16 > length)
        {
            return length;
        }

        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 4, 4));
        uint fieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 12, 4));
        return offset + 16 + (int)fieldsLength + (int)bodyLength;
    }

    private static bool IsAppearanceChanged(ReadOnlySpan<byte> fields)
    {
        int offset = 0;
        string? interfaceName = null;
        string? member = null;
        while (offset < fields.Length)
        {
            byte fieldLength = fields[offset++];
            if (fieldLength == 0)
            {
                break;
            }

            ReadOnlySpan<byte> field = fields.Slice(offset, fieldLength);
            offset += fieldLength;
            byte code = field[0];
            int valueOffset = 1;
            (string signature, object? value) = DbusWire.ReadVariant(field, ref valueOffset);
            if (code == DbusWire.HeaderFieldInterface && signature == "s" && value is string s)
            {
                interfaceName = s;
            }
            else if (code == DbusWire.HeaderFieldMember && signature == "s" && value is string m)
            {
                member = m;
            }
        }

        return interfaceName == "org.freedesktop.portal.Settings" && member == "SettingChanged";
    }

    private static Socket? TryConnect()
    {
        string? address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        string? path = ParseUnixPath(address);
        if (path is null)
        {
            return null;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    private static string? ParseUnixPath(string address)
    {
        foreach (string part in address.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("unix:path=", StringComparison.Ordinal))
            {
                return part["unix:path=".Length..];
            }

            if (part.StartsWith("unix:abstract=", StringComparison.Ordinal))
            {
                return "\0" + part["unix:abstract=".Length..];
            }
        }

        return null;
    }
}
