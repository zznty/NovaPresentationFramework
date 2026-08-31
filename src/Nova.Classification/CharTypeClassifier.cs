using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Classification;

/// <summary>
/// Win32 <c>GetStringTypeEx</c>-style character classification backed by the system ICU
/// (<c>libicuuc</c>) — the same database the in-tree classification tables are generated
/// from. The vendored WPF's <c>SelectionWordBreaker</c> classifies through this on Linux;
/// the returned bits honor the CT_CTYPE1/CT_CTYPE3 constants the word breaker reads.
/// When libicu is unavailable the classifier degrades to the BCL Unicode tables.
/// </summary>
[PublicAPI]
public static partial class CharTypeClassifier
{
    // CT_CTYPE1 bits (Win32 C1_*).
    private const ushort C1Upper = 0x0001;
    private const ushort C1Lower = 0x0002;
    private const ushort C1Digit = 0x0004;
    private const ushort C1Space = 0x0008;
    private const ushort C1Punct = 0x0010;
    private const ushort C1Blank = 0x0040;

    // CT_CTYPE3 bits (Win32 C3_*).
    private const ushort C3NonSpacing = 0x0001;
    private const ushort C3Diacritic = 0x0002;
    private const ushort C3Katakana = 0x0010;
    private const ushort C3Hiragana = 0x0020;
    private const ushort C3Halfwidth = 0x0040;
    private const ushort C3Fullwidth = 0x0080;
    private const ushort C3Ideograph = 0x0100;
    private const ushort C3Kashida = 0x0200;

    private const uint CtCtype1 = 1;

    // uchar.h UProperty selectors.
    private const int UcharScript = 0x100A;
    private const int UcharEastAsianWidth = 0x1004;

    // uscript.h script codes the word breaker cares about.
    private const int ScriptHiragana = 20;
    private const int ScriptKatakana = 21;

    // uchar.h UEastAsianWidth values.
    private const int EaHalfwidth = 2;
    private const int EaFullwidth = 3;
    private const int EaWide = 4;

    private static int _icuMissing;

    /// <summary>
    /// Classifies each UTF-16 code unit of <paramref name="source"/> into
    /// <paramref name="types"/> (same length) using <paramref name="infoType"/>'s bit
    /// vocabulary (CT_CTYPE1 or CT_CTYPE3).
    /// </summary>
    public static void GetStringType(uint infoType, ReadOnlySpan<char> source, Span<ushort> types)
    {
        for (int i = 0; i < source.Length; i++)
        {
            types[i] = Classify(infoType, source[i]);
        }
    }

    private static ushort Classify(uint infoType, char ch)
    {
        if (Volatile.Read(ref _icuMissing) == 0)
        {
            try
            {
                return infoType == CtCtype1 ? ClassifyCtype1(ch) : ClassifyCtype3(ch);
            }
            catch (DllNotFoundException)
            {
                Volatile.Write(ref _icuMissing, 1);
            }
            catch (EntryPointNotFoundException)
            {
                Volatile.Write(ref _icuMissing, 1);
            }
        }

        return infoType == CtCtype1 ? ClassifyCtype1Bcl(ch) : ClassifyCtype3Bcl(ch);
    }

    private static ushort ClassifyCtype1(char ch)
    {
        ushort type = 0;
        if (IsUpper(ch) != 0)
        {
            type |= C1Upper;
        }

        if (IsLower(ch) != 0)
        {
            type |= C1Lower;
        }

        if (IsDigit(ch) != 0)
        {
            type |= C1Digit;
        }

        if (IsSpace(ch) != 0)
        {
            type |= C1Space;
        }

        if (IsPunct(ch) != 0)
        {
            type |= C1Punct;
        }

        if (IsBlank(ch) != 0)
        {
            type |= C1Blank;
        }

        return type;
    }

    private static ushort ClassifyCtype3(char ch)
    {
        ushort type = 0;
        int script = GetIntPropertyValue(ch, UcharScript);
        int width = GetIntPropertyValue(ch, UcharEastAsianWidth);
        if (script == ScriptHiragana)
        {
            type |= C3Hiragana;
        }
        else if (script == ScriptKatakana)
        {
            type |= C3Katakana;
        }

        if (width == EaHalfwidth && script == ScriptKatakana)
        {
            type |= C3Halfwidth;
        }
        else if (width is EaFullwidth or EaWide)
        {
            type |= C3Fullwidth;
        }

        // The word breaker treats the CJK scripts as ideographs; the UCHAR_IDEOGRAPHIC
        // int property matches the Win32 C3_IDEOGRAPH semantics.
        if (GetIntPropertyValue(ch, 17 /* UCHAR_IDEOGRAPHIC */) != 0)
        {
            type |= C3Ideograph;
        }

        if (ch is >= '\u0300' and <= '\u036F')
        {
            type |= C3NonSpacing | C3Diacritic;
        }

        if (ch == '\u0640')
        {
            type |= C3Kashida;
        }

        return type;
    }

    private static ushort ClassifyCtype1Bcl(char ch)
    {
        ushort type = 0;
        if (char.IsUpper(ch))
        {
            type |= C1Upper;
        }
        if (char.IsLower(ch))
        {
            type |= C1Lower;
        }
        if (char.IsDigit(ch))
        {
            type |= C1Digit;
        }
        if (char.IsWhiteSpace(ch))
        {
            type |= C1Space;
        }
        if (char.IsPunctuation(ch) || char.IsSymbol(ch))
        {
            type |= C1Punct;
        }
        if (ch is ' ' or '\t')
        {
            type |= C1Blank;
        }
        return type;
    }

    private static ushort ClassifyCtype3Bcl(char ch)
    {
        ushort type = 0;
        if (ch is >= '\u3040' and <= '\u309F')
        {
            type |= C3Hiragana;
        }
        else if (ch is >= '\u30A0' and <= '\u30FF')
        {
            type |= C3Katakana;
        }
        else if (ch is >= '\uFF65' and <= '\uFF9F')
        {
            type |= C3Halfwidth | C3Katakana;
        }
        else if (ch is >= '\uFF01' and <= '\uFF60')
        {
            type |= C3Fullwidth;
        }
        else if (ch is >= '\u3400' and <= '\u9FFF')
        {
            type |= C3Ideograph;
        }

        if (ch is >= '\u0300' and <= '\u036F')
        {
            type |= C3NonSpacing | C3Diacritic;
        }

        if (ch == '\u0640')
        {
            type |= C3Kashida;
        }

        return type;
    }

    [LibraryImport("icuuc", EntryPoint = "u_isupper_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsUpper(int c);

    [LibraryImport("icuuc", EntryPoint = "u_islower_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsLower(int c);

    [LibraryImport("icuuc", EntryPoint = "u_isdigit_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsDigit(int c);

    [LibraryImport("icuuc", EntryPoint = "u_isspace_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsSpace(int c);

    [LibraryImport("icuuc", EntryPoint = "u_isblank_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsBlank(int c);

    [LibraryImport("icuuc", EntryPoint = "u_ispunct_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial byte IsPunct(int c);

    [LibraryImport("icuuc", EntryPoint = "u_getIntPropertyValue_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetIntPropertyValue(int c, int property);
}
