using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.FreeType;

[PublicAPI]
public sealed unsafe partial class FontFace : IDisposable
{
    private const string NativeLibrary = "freetype";
    private const int LoadNoScale = 0x1;
    private const int LoadNoBitmap = 0x8;
    private const int LoadRender = 0x4;
    private const int PixelModeGray = 2;

    internal FontFace()
    {
    }

    private static readonly ConcurrentDictionary<nint, FontFace> s_live = new();

    public IntPtr NativeFaceHandle { get; internal set; }

    /// <summary>
    /// Looks up a live face by <see cref="NativeFaceHandle"/>. WPF writes that pointer
    /// into <c>MILCMD_GLYPHRUN_CREATE.pIDWriteFont</c>.
    /// </summary>
    public static bool TryGet(nint nativeHandle, [NotNullWhen(true)] out FontFace? face)
    {
        return s_live.TryGetValue(nativeHandle, out face);
    }

    internal static void RegisterLive(FontFace face)
    {
        if (face.NativeFaceHandle != IntPtr.Zero)
        {
            s_live[face.NativeFaceHandle] = face;
        }
    }

    internal static void UnregisterLive(FontFace face)
    {
        if (face.NativeFaceHandle != IntPtr.Zero)
        {
            _ = s_live.TryRemove(face.NativeFaceHandle, out _);
        }
    }

    internal GCHandle MemoryPin { get; set; }

    internal FreeTypeLibrary? Library { get; set; }

    public string FamilyName { get; internal set; } = string.Empty;

    public string StyleName { get; internal set; } = string.Empty;

    public FontFaceMetrics Metrics { get; internal set; }

    public uint GetGlyphIndex(uint codepoint)
    {
        EnsureUsable();
        return FT_Get_Char_Index(NativeFaceHandle, codepoint);
    }

    public GlyphMetrics GetDesignMetrics(uint glyphIndex)
    {
        EnsureUsable();
        if (FT_Load_Glyph(NativeFaceHandle, glyphIndex, LoadNoBitmap | LoadNoScale) != 0)
        {
            throw new FreeTypeException($"failed to load design metrics for glyph {glyphIndex}.");
        }

        {
            NativeFace* face = (NativeFace*)NativeFaceHandle;
            NativeGlyphMetrics metrics = face->Glyph->Metrics;
            double width = metrics.Width;
            double height = metrics.Height;
            return new GlyphMetrics(
                glyphIndex,
                new Size(metrics.HoriAdvance, metrics.VertAdvance),
                new Rect(metrics.HoriBearingX, metrics.HoriBearingY - height, width, height));
        }
    }

    public GlyphBitmap Rasterize(uint glyphIndex, double pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);

        EnsureUsable();
        uint pixelHeight = (uint)Math.Max(1, Math.Round(pixelSize, MidpointRounding.AwayFromZero));
        if (FT_Set_Pixel_Sizes(NativeFaceHandle, 0, pixelHeight) != 0)
        {
            throw new FreeTypeException("failed to set the face pixel size.");
        }

        if (FT_Load_Glyph(NativeFaceHandle, glyphIndex, LoadRender) != 0)
        {
            throw new FreeTypeException($"failed to rasterize glyph {glyphIndex}.");
        }

        {
            NativeFace* face = (NativeFace*)NativeFaceHandle;
            NativeBitmap bitmap = face->Glyph->Bitmap;
            if (bitmap.Rows == 0 || bitmap.Width == 0)
            {
                // Whitespace glyphs (e.g. space) produce an empty bitmap: no pixels, no buffer.
                return new GlyphBitmap(new PixelSize(0, 0), 0, 0, 0, []);
            }

            if (bitmap.PixelMode != PixelModeGray || bitmap.Buffer == IntPtr.Zero)
            {
                throw new FreeTypeException($"glyph {glyphIndex} produced no gray bitmap.");
            }

            long byteCount = bitmap.Rows * Math.Abs(bitmap.Pitch);
            byte[] pixels = new byte[byteCount];
            if (byteCount > 0)
            {
                Marshal.Copy(bitmap.Buffer, pixels, 0, checked((int)byteCount));
            }

            // The hinted bitmap_left derives from the font's hmtx left-side bearing
            // (which the icon font now carries centered), so the hinted placement keeps
            // the pixel-grid evenness for text while the icons stay centered.
            return new GlyphBitmap(new PixelSize((int)bitmap.Width, (int)bitmap.Rows), face->Glyph->BitmapLeft, face->Glyph->BitmapTop, bitmap.Pitch, pixels);
        }
    }

    public bool TryGetTable(uint tag, out byte[] table)
    {
        EnsureUsable();
        ulong length = 0;
        if (FT_Load_Sfnt_Table(NativeFaceHandle, tag, 0, IntPtr.Zero, ref length) != 0 || length == 0)
        {
            table = [];
            return false;
        }

        byte[] buffer = new byte[length];
        {
            fixed (byte* bufferStart = buffer)
            {
                if (FT_Load_Sfnt_Table(NativeFaceHandle, tag, 0, (IntPtr)bufferStart, ref length) != 0)
                {
                    table = [];
                    return false;
                }
            }
        }

        table = buffer;
        return true;
    }

    public void SetPixelSize(double pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        uint pixelHeight = (uint)Math.Max(1, Math.Round(pixelSize, MidpointRounding.AwayFromZero));
        SetPixelSize(pixelHeight);
    }

    public void Dispose()
    {
        if (NativeFaceHandle == IntPtr.Zero)
        {
            return;
        }

        if (Library is { IsDisposed: false })
        {
            Library.Unregister(this);
            UnregisterLive(this);
            IgnoreResult(FT_Done_Face(NativeFaceHandle));
        }

        NativeFaceHandle = IntPtr.Zero;
        if (MemoryPin.IsAllocated)
        {
            MemoryPin.Free();
        }
    }

    internal bool HasPixelSize
    {
        get
        {
            if (NativeFaceHandle == IntPtr.Zero)
            {
                return false;
            }

            {
                NativeFace* face = (NativeFace*)NativeFaceHandle;
                return face->Size != 0 && ((NativeSize*)face->Size)->XScale != 0;
            }
        }
    }

    internal void SetPixelSize(uint pixelHeight)
    {
        EnsureUsable();
        if (FT_Set_Pixel_Sizes(NativeFaceHandle, 0, pixelHeight) != 0)
        {
            throw new FreeTypeException("failed to set the face pixel size.");
        }
    }

    internal void RefreshMetadata()
    {
        {
            NativeFace* face = (NativeFace*)NativeFaceHandle;
            FamilyName = face->FamilyName == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)face->FamilyName) ?? string.Empty;
            StyleName = face->StyleName == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)face->StyleName) ?? string.Empty;
            Metrics = new FontFaceMetrics(
                face->UnitsPerEm,
                face->Ascender,
                face->Descender,
                face->Height - face->Ascender + face->Descender,
                (ushort)face->NumGlyphs);
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(NativeFaceHandle == IntPtr.Zero, this);
    }

    private static void IgnoreResult<T>(T _)
    {
    }

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint FT_Get_Char_Index(IntPtr face, ulong charcode);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Done_Face(IntPtr face);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Set_Pixel_Sizes(IntPtr face, uint pixelWidth, uint pixelHeight);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Load_Glyph(IntPtr face, uint glyphIndex, int loadFlags);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FT_Load_Sfnt_Table(IntPtr face, ulong tag, long offset, IntPtr buffer, ref ulong length);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFace
    {
        public nint NumFaces;
        public nint FaceIndex;
        public nint FaceFlags;
        public nint StyleFlags;
        public nint NumGlyphs;
        public byte* FamilyName;
        public byte* StyleName;
        public int NumFixedSizes;
        public nint AvailableSizes;
        public int NumCharmaps;
        public nint Charmaps;
        public nint GenericData;
        public nint GenericFinalizer;
        public long BboxXMin;
        public long BboxYMin;
        public long BboxXMax;
        public long BboxYMax;
        public ushort UnitsPerEm;
        public short Ascender;
        public short Descender;
        public short Height;
        public short MaxAdvanceWidth;
        public short MaxAdvanceHeight;
        public short UnderlinePosition;
        public short UnderlineThickness;
        public NativeGlyphSlot* Glyph;
        public nint Size;
        public nint Charmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGlyphSlot
    {
        public nint Library;
        public nint Face;
        public nint Next;
        public uint GlyphIndex;
        public nint GenericData;
        public nint GenericFinalizer;
        public NativeGlyphMetrics Metrics;
        public long LinearHoriAdvance;
        public long LinearVertAdvance;
        public NativeVector Advance;
        public int Format;
        public NativeBitmap Bitmap;
        public int BitmapLeft;
        public int BitmapTop;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public nint Face;
        public nint GenericData;
        public nint GenericFinalizer;
        public ushort XPpem;
        public ushort YPpem;
        public long XScale;
        public long YScale;
        public long Ascender;
        public long Descender;
        public long Height;
        public long MaxAdvance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGlyphMetrics
    {
        public long Width;
        public long Height;
        public long HoriBearingX;
        public long HoriBearingY;
        public long HoriAdvance;
        public long VertBearingX;
        public long VertBearingY;
        public long VertAdvance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeVector
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public uint Rows;
        public uint Width;
        public int Pitch;
        public IntPtr Buffer;
        public ushort NumGrays;
        public byte PixelMode;
        public byte PaletteMode;
        public IntPtr Palette;
    }
}
