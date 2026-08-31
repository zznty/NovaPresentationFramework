using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.FontConfig;

/// <summary>Safe fontconfig family resolution. Native P/Invoke stays in this assembly.</summary>
[PublicAPI]
public sealed unsafe partial class FontConfigLibrary : IDisposable
{
    private const string NativeLibrary = "fontconfig";

    private static readonly byte[] FamilyObject = "family"u8.ToArray();
    private static readonly byte[] FileObject = "file"u8.ToArray();
    private static readonly byte[] IndexObject = "index"u8.ToArray();
    private static readonly byte[] WeightObject = "weight"u8.ToArray();
    private static readonly byte[] SlantObject = "slant"u8.ToArray();
    private static readonly byte[] WidthObject = "width"u8.ToArray();

    private bool _disposed;

    /// <summary>Font files registered via <see cref="RegisterAppFont"/>; re-applied on every
    /// <see cref="FontConfigLibrary"/> construction (each ctor re-runs FcInit, which rebuilds
    /// the global config, so registered app fonts would otherwise be lost on FcFini/dispose).</summary>
    private static readonly List<string> AppFonts = [];

    public FontConfigLibrary()
    {
        if (FcInit() == 0)
        {
            throw new FontConfigException("fontconfig initialization failed.");
        }

        RegisterBundledFonts();
        ReapplyAppFonts();
        IsInitialized = true;
    }

    /// <summary>
    /// Registers the NPF bundle's bundled icon font (shipped as
    /// <c>fonts/NovaFluentIcons.ttf</c> next to the app) with the fontconfig
    /// configuration, so its family name resolves from the system collection — the
    /// equivalent of the Windows system font registry carrying Segoe Fluent Icons.
    /// A fixed file, not a directory scan: the port ships exactly this font.
    /// </summary>
    private static void RegisterBundledFonts()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fonts", "NovaFluentIcons.ttf");
        if (!File.Exists(path))
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);
        lock (AppFonts)
        {
            if (!AppFonts.Contains(fullPath, StringComparer.Ordinal))
            {
                AppFonts.Add(fullPath);
                _ = AddFontToConfig(fullPath);
            }
        }
    }

    /// <summary>
    /// Registers a font file with the process fontconfig configuration so family queries can
    /// resolve it (e.g. the bundled "Nova Fluent Icons" icon font). The registration survives
    /// later <see cref="FontConfigLibrary"/> construction (each ctor re-applies the registered
    /// paths after FcInit). Throws if the file is missing or fontconfig rejects it, so a broken
    /// bundle fails loudly instead of silently resolving tofu.
    /// </summary>
    public static void RegisterAppFont(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FontConfigException($"app font file not found: {fullPath}");
        }

        lock (AppFonts)
        {
            if (!AppFonts.Contains(fullPath, StringComparer.Ordinal))
            {
                AppFonts.Add(fullPath);
            }
        }

        if (AddFontToConfig(fullPath) == 0)
        {
            throw new FontConfigException($"fontconfig rejected app font '{fullPath}'");
        }
    }

    /// <summary>
    /// The Fluent theme names the Windows-only Segoe icon families; map them to the
    /// bundled NovaFluentIcons so bare family names resolve on Linux the way the Windows
    /// system font registry resolves them. Composite lists ("A, B") are mapped part-wise.
    /// </summary>
    private static string MapFamily(string family)
    {
        string[] parts = family.Split(',');
        bool mapped = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Equals("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "Nova Fluent Icons";
                mapped = true;
            }
        }

        return mapped ? string.Join(", ", parts) : family;
    }

    private static void ReapplyAppFonts()
    {
        lock (AppFonts)
        {
            foreach (string font in AppFonts)
            {
                _ = AddFontToConfig(font);
            }
        }
    }

    private static int AddFontToConfig(string path)
    {
        using var file = NativeUtf8.FromString(path);
        return FcConfigAppFontAddFile(IntPtr.Zero, file.Pointer);
    }

    public bool IsInitialized { get; private set; }

    public FontMatch Match(FontQuery query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var family = NativeUtf8.FromString(MapFamily(query.Family));
        IntPtr pattern = FcPatternCreate();
        if (pattern == IntPtr.Zero)
        {
            throw new FontConfigException("failed to create a fontconfig pattern.");
        }

        try
        {
            if (PatternAddString(pattern, FamilyObject, family.Pointer) == 0)
            {
                throw new FontConfigException("failed to set the family on the pattern.");
            }

            if (PatternAddInteger(pattern, WeightObject, query.Weight) == 0 ||
                PatternAddInteger(pattern, SlantObject, query.Slant) == 0 ||
                PatternAddInteger(pattern, WidthObject, query.Width) == 0)
            {
                throw new FontConfigException("failed to set the style on the pattern.");
            }

            if (FcConfigSubstitute(IntPtr.Zero, pattern, FcMatchKind.Pattern) == 0)
            {
                throw new FontConfigException("fontconfig pattern substitution failed.");
            }

            FcDefaultSubstitute(pattern);

            IntPtr match = FcFontMatch(IntPtr.Zero, pattern, out FcResult result);
            if (match == IntPtr.Zero || result != FcResult.Match)
            {
                throw new FontConfigException($"no font matched the family '{query.Family}'.");
            }

            try
            {
                return ReadMatch(match, query.Family);
            }
            finally
            {
                FcPatternDestroy(match);
            }
        }
        finally
        {
            FcPatternDestroy(pattern);
        }
    }

    public int ListFamilies(Span<string> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr objectSet = FcObjectSetCreate();
        IntPtr pattern = FcPatternCreate();
        IntPtr fontSet = IntPtr.Zero;
        try
        {
            if (objectSet == IntPtr.Zero)
            {
                throw new FontConfigException("failed to create a fontconfig object set.");
            }

            if (pattern == IntPtr.Zero)
            {
                throw new FontConfigException("failed to create a fontconfig pattern.");
            }

            if (ObjectSetAdd(objectSet, FamilyObject) == 0)
            {
                throw new FontConfigException("failed to request the family property.");
            }

            fontSet = FcFontList(IntPtr.Zero, pattern, objectSet);
            if (fontSet == IntPtr.Zero)
            {
                throw new FontConfigException("fontconfig failed to enumerate fonts.");
            }

            {
                FcFontSetNative* set = (FcFontSetNative*)fontSet;
                HashSet<string> seen = new(StringComparer.Ordinal);
                int written = 0;
                for (int i = 0; i < set->NFont && written < destination.Length; i++)
                {
                    if (PatternGetString(set->Fonts[i], FamilyObject, 0, out IntPtr family) != FcResult.Match)
                    {
                        continue;
                    }

                    string name = Marshal.PtrToStringUTF8(family) ?? string.Empty;
                    if (name.Length > 0 && seen.Add(name))
                    {
                        destination[written++] = name;
                    }
                }

                return written;
            }
        }
        finally
        {
            if (fontSet != IntPtr.Zero)
            {
                FcFontSetDestroy(fontSet);
            }

            if (pattern != IntPtr.Zero)
            {
                FcPatternDestroy(pattern);
            }

            FcObjectSetDestroy(objectSet);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsInitialized = false;
        FcFini();
    }

    private static FontMatch ReadMatch(IntPtr pattern, string fallbackFamily)
    {
        string family = PatternGetString(pattern, FamilyObject, 0, out IntPtr familyPtr) == FcResult.Match
            ? Marshal.PtrToStringUTF8(familyPtr) ?? fallbackFamily
            : fallbackFamily;

        if (PatternGetString(pattern, FileObject, 0, out IntPtr filePtr) != FcResult.Match)
        {
            throw new FontConfigException("the matched font has no file path.");
        }

        string filePath = Marshal.PtrToStringUTF8(filePtr) ?? string.Empty;
        if (filePath.Length == 0)
        {
            throw new FontConfigException("the matched font has an empty file path.");
        }

        _ = PatternGetInteger(pattern, IndexObject, 0, out int faceIndex);
        _ = PatternGetInteger(pattern, WeightObject, 0, out int weight);
        _ = PatternGetInteger(pattern, SlantObject, 0, out int slant);
        _ = PatternGetInteger(pattern, WidthObject, 0, out int width);
        return new FontMatch(family, filePath, faceIndex, weight, slant, width);
    }

    private static int PatternAddString(IntPtr pattern, byte[] objectName, IntPtr value)
    {
        fixed (byte* name = objectName)
        {
            return FcPatternAddString(pattern, name, value);
        }
    }

    private static int PatternAddInteger(IntPtr pattern, byte[] objectName, int value)
    {
        fixed (byte* name = objectName)
        {
            return FcPatternAddInteger(pattern, name, value);
        }
    }

    private static FcResult PatternGetString(IntPtr pattern, byte[] objectName, int id, out IntPtr value)
    {
        fixed (byte* name = objectName)
        {
            return FcPatternGetString(pattern, name, id, out value);
        }
    }

    private static FcResult PatternGetInteger(IntPtr pattern, byte[] objectName, int id, out int value)
    {
        fixed (byte* name = objectName)
        {
            return FcPatternGetInteger(pattern, name, id, out value);
        }
    }

    private static int ObjectSetAdd(IntPtr objectSet, byte[] objectName)
    {
        fixed (byte* name = objectName)
        {
            return FcObjectSetAdd(objectSet, name);
        }
    }

    private enum FcResult
    {
        Match = 0
    }

    private enum FcMatchKind
    {
        Pattern = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FcFontSetNative
    {
        public int NFont;
        public int SFont;
        public nint* Fonts;
    }

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcInit();

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FcFini();

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr FcPatternCreate();

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcConfigAppFontAddFile(IntPtr config, IntPtr file);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcPatternAddString(IntPtr pattern, byte* objectName, IntPtr value);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcPatternAddInteger(IntPtr pattern, byte* objectName, int value);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcConfigSubstitute(IntPtr config, IntPtr pattern, FcMatchKind kind);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FcDefaultSubstitute(IntPtr pattern);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr FcFontMatch(IntPtr config, IntPtr pattern, out FcResult result);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial FcResult FcPatternGetString(IntPtr pattern, byte* objectName, int id, out IntPtr value);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial FcResult FcPatternGetInteger(IntPtr pattern, byte* objectName, int id, out int value);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr FcFontList(IntPtr config, IntPtr pattern, IntPtr objectSet);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FcFontSetDestroy(IntPtr fontSet);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr FcObjectSetCreate();

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FcObjectSetAdd(IntPtr objectSet, byte* objectName);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FcObjectSetDestroy(IntPtr objectSet);

    [LibraryImport(NativeLibrary)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FcPatternDestroy(IntPtr pattern);

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
