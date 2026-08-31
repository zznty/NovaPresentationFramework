using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.FreeType;
using Nova.Geometry;

namespace Nova.HarfBuzz;

/// <summary>Shapes text against an open <see cref="FontFace"/>. Native P/Invoke stays in this assembly.</summary>
[PublicAPI]
public sealed unsafe partial class HarfBuzzShaper : IDisposable
{
    private const string NativeLibrary = "harfbuzz";
    private const int DirectionLtr = 4;
    private const int DirectionRtl = 5;
    private const double FontUnitsToPixels = 1.0 / 64.0;

    private IntPtr _font;
    private IntPtr _buffer;
    private bool _disposed;

    public HarfBuzzShaper(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        Face = face;

        // hb_ft_font_create_referenced shapes at the FT face's current pixel size;
        // default to 16px when the face has no size set yet.
        if (!face.HasPixelSize)
        {
            face.SetPixelSize(16);
        }

        _font = HbFtFontCreateReferenced(face.NativeFaceHandle);
        if (_font == IntPtr.Zero)
        {
            throw new HarfBuzzException("failed to create the HarfBuzz font.");
        }

        _buffer = HbBufferCreate();
        if (_buffer != IntPtr.Zero)
        {
            return;
        }

        HbFontDestroy(_font);
        _font = IntPtr.Zero;
        throw new HarfBuzzException("failed to create the HarfBuzz buffer.");
    }

    public FontFace Face { get; }

    public int Shape(ReadOnlySpan<char> text, ShapeOptions options, Span<ShapedGlyph> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HbBufferClearContents(_buffer);
        {
            fixed (char* chars = text)
            {
                HbBufferAddUtf16(_buffer, (ushort*)chars, text.Length, 0, -1);
            }
        }

        HbBufferSetDirection(_buffer, options.RightToLeft ? DirectionRtl : DirectionLtr);
        using var script = NativeUtf8.FromString(options.Script);
        HbBufferSetScript(_buffer, HbScriptFromString(script.Pointer, -1));
        using var language = NativeUtf8.FromString(options.Language);
        HbBufferSetLanguage(_buffer, HbLanguageFromString(language.Pointer, -1));

        HbShape(_font, _buffer, IntPtr.Zero, 0);

        {
            uint infoLength = 0;
            uint positionLength = 0;
            HbGlyphInfo* infos = HbBufferGetGlyphInfos(_buffer, &infoLength);
            HbGlyphPosition* positions = HbBufferGetGlyphPositions(_buffer, &positionLength);
            int count = Math.Min(destination.Length, (int)infoLength);
            for (int i = 0; i < count; i++)
            {
                HbGlyphInfo info = infos[i];
                HbGlyphPosition position = positions[i];
                destination[i] = new ShapedGlyph(
                    info.Codepoint,
                    info.Cluster,
                    new Point(position.XOffset * FontUnitsToPixels, position.YOffset * FontUnitsToPixels),
                    new Size(position.XAdvance * FontUnitsToPixels, position.YAdvance * FontUnitsToPixels));
            }

            return count;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
        if (_buffer != IntPtr.Zero)
        {
            HbBufferDestroy(_buffer);
            _buffer = IntPtr.Zero;
        }

        if (_font == IntPtr.Zero)
        {
            return;
        }

        HbFontDestroy(_font);
        _font = IntPtr.Zero;
    }

    ~HarfBuzzShaper()
    {
        Dispose();
    }

    [LibraryImport(NativeLibrary, EntryPoint = "hb_ft_font_create_referenced")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr HbFtFontCreateReferenced(IntPtr face);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_font_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbFontDestroy(IntPtr font);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_create")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr HbBufferCreate();

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferDestroy(IntPtr buffer);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_clear_contents")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferClearContents(IntPtr buffer);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_add_utf16")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferAddUtf16(IntPtr buffer, ushort* text, int textLength, uint itemOffset, int itemLength);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_set_direction")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferSetDirection(IntPtr buffer, int direction);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_set_script")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferSetScript(IntPtr buffer, uint script);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_set_language")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbBufferSetLanguage(IntPtr buffer, IntPtr language);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_script_from_string")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint HbScriptFromString(IntPtr script, int length);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_language_from_string")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr HbLanguageFromString(IntPtr language, int length);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_shape")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HbShape(IntPtr font, IntPtr buffer, IntPtr features, uint featureCount);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_get_glyph_infos")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial HbGlyphInfo* HbBufferGetGlyphInfos(IntPtr buffer, uint* length);

    [LibraryImport(NativeLibrary, EntryPoint = "hb_buffer_get_glyph_positions")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial HbGlyphPosition* HbBufferGetGlyphPositions(IntPtr buffer, uint* length);

    [StructLayout(LayoutKind.Sequential)]
    private struct HbGlyphInfo
    {
        public uint Codepoint;
        public uint Mask;
        public uint Cluster;
        public uint Var1;
        public uint Var2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HbGlyphPosition
    {
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        public int Var;
    }

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
