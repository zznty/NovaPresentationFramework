using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Nova.DesktopTheme;

/// <summary>
/// Minimal DBus session-bus client (unix socket) for <c>org.freedesktop.portal.Settings</c>.
/// One-shot <see cref="TryReadAppearance"/> covers the startup palette; the same connection
/// machinery is reused by the change monitor for the <c>SettingChanged</c> signal. Any
/// failure — no <c>DBUS_SESSION_BUS_ADDRESS</c>, no daemon, auth rejection, parse error —
/// returns <c>null</c> and the theme chain falls through to kdeglobals/Trolltech/GTK.
/// </summary>
internal static class DbusConnection
{
    private const string PortalDestination = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string SettingsInterface = "org.freedesktop.portal.Settings";

    /// <summary>
    /// Reads one <c>org.freedesktop.appearance</c> key via the portal. Returns a <c>uint</c>
    /// for <c>color-scheme</c>, a <c>double[3]</c> for <c>accent-color</c>, or <c>null</c>
    /// when the portal is absent or the value is unparseable. Never throws.
    /// </summary>
    public static object? TryReadAppearance(string namespaceName, string key)
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            return null;
        }

        byte[]? reply = connection.Call(
            PortalDestination,
            PortalPath,
            SettingsInterface,
            "Read",
            "ss",
            [namespaceName, key]);
        if (reply is null)
        {
            return null;
        }

        try
        {
            (string signature, object? value) = DbusWire.ParseVariantReply(reply);
            return signature == "u" && value is uint u ? u
                : signature == "(ddd)" && value is double[] d ? d
                : null;
        }
        catch (DbusProtocolException)
        {
            return null;
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly Socket _socket;
        private uint _serial;
        private readonly byte[] _buffer = new byte[64 * 1024];

        public Session(Socket socket)
        {
            _socket = socket;
            _socket.ReceiveTimeout = 5000;
            _socket.SendTimeout = 5000;
        }

        /// <summary>
        /// Sends a method call and returns the raw method-return body (or <c>null</c> on
        /// timeout/error/EOF). Skips interleaved signals.
        /// </summary>
        public byte[]? Call(string destination, string path, string interfaceName, string member, string signature, object?[] args)
        {
            uint serial = NextSerial();
            byte[] message = DbusWire.BuildMethodCall(serial, destination, path, interfaceName, member, signature, args);
            try
            {
                _ = _socket.Send(message);
            }
            catch (SocketException)
            {
                return null;
            }

            try
            {
                while (true)
                {
                    (byte type, uint replySerial, byte[] body) = ReadMessage();
                    if (type == DbusWire.MessageTypeMethodReturn && replySerial == serial)
                    {
                        return body;
                    }

                    if (type == DbusWire.MessageTypeError && replySerial == serial)
                    {
                        return null;
                    }
                }
            }
            catch (SocketException)
            {
                return null;
            }
            catch (DbusProtocolException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _socket.Dispose();
        }

        public void SendRaw(byte[] data)
        {
            _ = _socket.Send(data);
        }

        public string ReadLine()
        {
            int length = 0;
            while (true)
            {
                int n = _socket.Receive(_buffer, length, 1, SocketFlags.None);
                if (n <= 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                if (_buffer[length] == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(_buffer, 0, length).TrimEnd('\r');
                }

                length++;
                if (length >= _buffer.Length)
                {
                    throw new DbusProtocolException("auth line too long");
                }
            }
        }

        private uint NextSerial()
        {
            return ++_serial;
        }

        private (byte Type, uint ReplySerial, byte[] Body) ReadMessage()
        {
            ReadExactly(16);
            if (_buffer[0] != 0x6c)
            {
                throw new DbusProtocolException("big-endian bus not supported");
            }

            byte type = _buffer[1];
            uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(4, 4));
            _ = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(8, 4));
            uint fieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(12, 4));
            ReadExactly((int)fieldsLength);
            uint replySerial = 0;
            ParseHeaderFields(_buffer.AsSpan(16, (int)fieldsLength), ref replySerial);
            byte[] body = new byte[bodyLength];
            ReadExactly((int)bodyLength, body);
            return (type, replySerial, body);
        }

        private void ReadExactly(int count, byte[]? destination = null)
        {
            int received = 0;
            while (received < count)
            {
                int n = destination is null
                    ? _socket.Receive(_buffer, received, count - received, SocketFlags.None)
                    : _socket.Receive(destination, received, count - received, SocketFlags.None);
                if (n <= 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                received += n;
            }
        }

        private static void ParseHeaderFields(ReadOnlySpan<byte> fields, ref uint replySerial)
        {
            int offset = 0;
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
                if (code == DbusWire.HeaderFieldReplySerial && signature == "u" && value is uint u)
                {
                    replySerial = u;
                }
            }
        }
    }

    private static Session? TryConnect()
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
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }

        var session = new Session(socket);
        if (!Authenticate(session))
        {
            session.Dispose();
            return null;
        }

        // Hello establishes the connection so service calls route; ignore the reply.
        _ = session.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", string.Empty, []);
        return session;
    }

    private static bool Authenticate(Session session)
    {
        try
        {
            string uidHex = Convert.ToHexString(Encoding.ASCII.GetBytes(GetUserId().ToString(CultureInfo.InvariantCulture)));
            byte[] auth = Encoding.ASCII.GetBytes($"\0AUTH EXTERNAL {uidHex}\r\n");
            session.SendRaw(auth);
            string response = session.ReadLine();
            if (!response.StartsWith("OK ", StringComparison.Ordinal))
            {
                return false;
            }

            session.SendRaw(Encoding.ASCII.GetBytes("BEGIN\r\n"));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (DbusProtocolException)
        {
            return false;
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

    private static uint GetUserId()
    {
        try
        {
            return GetUserIdNative();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return 0;
        }
    }

    [DllImport("libc", EntryPoint = "getuid", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetUserIdNative();
}
