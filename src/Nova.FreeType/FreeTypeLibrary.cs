using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.FreeType;

/// <summary>Owns an FT_Library. Native P/Invoke stays in this assembly.</summary>
[PublicAPI]
public sealed partial class FreeTypeLibrary : IDisposable
{
    private const string NativeLibrary = "freetype";

    private readonly List<FontFace> _faces = [];
    private IntPtr _library;

    internal bool IsDisposed { get; private set; }

    public FreeTypeLibrary()
    {
        _library = FT_Init_FreeType(out IntPtr library) != 0
            ? throw new FreeTypeException("failed to initialize FreeType.")
            : library;
    }

    public FontFace OpenFace(string path, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

        using var utf8Path = NativeUtf8.FromString(path);
        int error = FT_New_Face(_library, utf8Path.Pointer, faceIndex, out IntPtr facePointer);
        return error != 0
            ? throw new FreeTypeException($"failed to open font face '{path}'.", error)
            : CreateFace(facePointer);
    }

    public FontFace OpenFace(ReadOnlySpan<byte> memory, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

        // Copy so the face never aliases caller-owned memory, then pin for the face lifetime.
        byte[] copy = memory.ToArray();
        var pin = GCHandle.Alloc(copy, GCHandleType.Pinned);
        int error = FT_New_Memory_Face(_library, pin.AddrOfPinnedObject(), copy.Length, faceIndex, out IntPtr face);
        if (error == 0)
        {
            return CreateFace(face, pin);
        }

        pin.Free();
        throw new FreeTypeException("failed to open the in-memory font face.", error);
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        while (_faces.Count > 0)
        {
            _faces[0].Dispose();
        }

        IsDisposed = true;
        if (_library == IntPtr.Zero)
        {
            return;
        }

        IgnoreResult(FT_Done_FreeType(_library));
        _library = IntPtr.Zero;
    }

    internal void Unregister(FontFace face)
    {
        IgnoreResult(_faces.Remove(face));
    }

    private static void IgnoreResult<T>(T _)
    {
    }

    private FontFace CreateFace(IntPtr face, GCHandle pin = default)
    {
        var fontFace = new FontFace
        {
            NativeFaceHandle = face,
            MemoryPin = pin,
            Library = this
        };
        fontFace.RefreshMetadata();
        FontFace.RegisterLive(fontFace);
        _faces.Add(fontFace);
        return fontFace;
    }

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Init_FreeType(out IntPtr library);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_New_Face(IntPtr library, IntPtr filePath, long faceIndex, out IntPtr face);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_New_Memory_Face(IntPtr library, IntPtr fileBase, long fileSize, long faceIndex, out IntPtr face);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Done_Face(IntPtr face);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Done_FreeType(IntPtr library);

    private sealed class NativeUtf8(IntPtr pointer) : IDisposable
    {
        public IntPtr Pointer { get; } = pointer;

        public static NativeUtf8 FromString(string value)
        {
            return new NativeUtf8(Marshal.StringToCoTaskMemUTF8(value));
        }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(Pointer);
            }
        }
    }
}
