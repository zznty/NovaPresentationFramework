using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Classification;

/// <summary>
/// Generates the WPF-private classification payload (<c>MILGetClassificationTables</c> content)
/// from the system ICU <c>libicuuc</c> library. Produces WPF numbering, not ICU numbering.
/// </summary>
[PublicAPI]
public static partial class ClassificationTableBuilder
{
    private const string NativeLibrary = "icuuc";

    // UProperty selectors (uchar.h).
    private const int UcharScript = 0x100A;
    private const int UcharIdeographic = 17;

    // UCharCategory (uchar.h).
    private const sbyte UUppercaseLetter = 1;
    private const sbyte UOtherLetter = 5;
    private const sbyte UNonSpacingMark = 6;
    private const sbyte UEnclosingMark = 7;
    private const sbyte UCombiningSpacingMark = 8;
    private const sbyte UDecimalDigitNumber = 9;
    private const sbyte USpaceSeparator = 12;
    private const sbyte UControlChar = 15;
    private const sbyte UFormatChar = 16;
    private const sbyte UDashPunctuation = 19;
    private const sbyte UStartPunctuation = 20;
    private const sbyte UEndPunctuation = 21;
    private const sbyte UConnectorPunctuation = 22;
    private const sbyte UOtherPunctuation = 23;
    private const sbyte UInitialPunctuation = 28;
    private const sbyte UFinalPunctuation = 29;

    // UCharDirection (uchar.h) values consumed by the classifier (the rest are positional
    // in DirectionMap).
    private const int ULeftToRight = 0;
    private const int URightToLeft = 1;
    private const int UEuropeanNumberSeparator = 3;
    private const int UEuropeanNumberTerminator = 4;
    private const int UArabicNumber = 5;
    private const int UCommonNumberSeparator = 6;
    private const int URightToLeftArabic = 13;
    private const int URightToLeftEmbedding = 14;
    private const int URightToLeftOverride = 15;
    private const int URightToLeftIsolate = 21;

    // UScriptCode (uscript.h) for scripts the WPF table names.
    private const int UScriptCommon = 0;
    private const int UScriptInherited = 1;
    private const int UScriptArabic = 2;
    private const int UScriptArmenian = 3;
    private const int UScriptBengali = 4;
    private const int UScriptBopomofo = 5;
    private const int UScriptCherokee = 6;
    private const int UScriptCoptic = 7;
    private const int UScriptCyrillic = 8;
    private const int UScriptDeseret = 9;
    private const int UScriptDevanagari = 10;
    private const int UScriptEthiopic = 11;
    private const int UScriptGeorgian = 12;
    private const int UScriptGothic = 13;
    private const int UScriptGreek = 14;
    private const int UScriptGujarati = 15;
    private const int UScriptGurmukhi = 16;
    private const int UScriptHan = 17;
    private const int UScriptHangul = 18;
    private const int UScriptHebrew = 19;
    private const int UScriptHiragana = 20;
    private const int UScriptKannada = 21;
    private const int UScriptKatakana = 22;
    private const int UScriptKhmer = 23;
    private const int UScriptLao = 24;
    private const int UScriptLatin = 25;
    private const int UScriptMalayalam = 26;
    private const int UScriptMongolian = 27;
    private const int UScriptMyanmar = 28;
    private const int UScriptOgham = 29;
    private const int UScriptOldItalic = 30;
    private const int UScriptOriya = 31;
    private const int UScriptRunic = 32;
    private const int UScriptSinhala = 33;
    private const int UScriptSyriac = 34;
    private const int UScriptTamil = 35;
    private const int UScriptTelugu = 36;
    private const int UScriptThaana = 37;
    private const int UScriptThai = 38;
    private const int UScriptTibetan = 39;
    private const int UScriptCanadianAboriginal = 40;
    private const int UScriptYi = 41;
    private const int UScriptTagalog = 42;
    private const int UScriptHanunoo = 43;
    private const int UScriptBuhid = 44;
    private const int UScriptTagbanwa = 45;
    private const int UScriptBraille = 46;
    private const int UScriptCypriot = 47;
    private const int UScriptLimbu = 48;
    private const int UScriptLinearB = 49;
    private const int UScriptOsmanya = 50;
    private const int UScriptShavian = 51;
    private const int UScriptTaiLe = 52;
    private const int UScriptUgaritic = 53;
    private const int UScriptKatakanaOrHiragana = 54;
    private const int UScriptBuginese = 55;
    private const int UScriptGlagolitic = 56;
    private const int UScriptKharoshthi = 57;
    private const int UScriptSylotiNagri = 58;
    private const int UScriptNewTaiLue = 59;
    private const int UScriptTifinagh = 60;
    private const int UScriptOldPersian = 61;

    // ICU UCharDirection -> WPF DirectionClass remap. WPF numbering differs from ICU after L/R:
    // EN->3 not 2, AN->2 not 5, AL->4 not 13, etc.
    private static readonly byte[] DirectionMap =
    [
        (byte)DirectionClass.Left, // 0 L
        (byte)DirectionClass.Right, // 1 R
        (byte)DirectionClass.EuropeanNumber, // 2 EN
        (byte)DirectionClass.EuropeanSeparator, // 3 ES
        (byte)DirectionClass.EuropeanTerminator, // 4 ET
        (byte)DirectionClass.ArabicNumber, // 5 AN
        (byte)DirectionClass.CommonSeparator, // 6 CS
        (byte)DirectionClass.ParagraphSeparator, // 7 B
        (byte)DirectionClass.SegmentSeparator, // 8 S
        (byte)DirectionClass.WhiteSpace, // 9 WS
        (byte)DirectionClass.OtherNeutral, // 10 ON
        (byte)DirectionClass.LeftToRightEmbedding, // 11 LRE
        (byte)DirectionClass.LeftToRightOverride, // 12 LRO
        (byte)DirectionClass.ArabicLetter, // 13 AL
        (byte)DirectionClass.RightToLeftEmbedding, // 14 RLE
        (byte)DirectionClass.RightToLeftOverride, // 15 RLO
        (byte)DirectionClass.PopDirectionalFormat, // 16 PDF
        (byte)DirectionClass.NonSpacingMark, // 17 NSM
        (byte)DirectionClass.BoundaryNeutral, // 18 BN
        (byte)DirectionClass.OtherNeutral, // 19 FSI (predates isolates)
        (byte)DirectionClass.OtherNeutral, // 20 LRI
        (byte)DirectionClass.OtherNeutral, // 21 RLI
        (byte)DirectionClass.OtherNeutral, // 22 PDI
    ];

    // ScriptCaretInfo from MS.Internal.ClassificationUtility: scripts whose marks need caret
    // clustering -> ComplexMarkClass.
    private static readonly bool[] ScriptCaretInfo =
    [
        false, // Default
        false, // Arabic
        false, // Armenian
        true, // Bengali
        false, // Bopomofo
        false, // Braille
        true, // Buginese
        false, // Buhid
        false, // CanadianSyllabics
        false, // Cherokee
        false, // CJKIdeographic
        false, // Coptic
        false, // CypriotSyllabary
        false, // Cyrillic
        false, // Deseret
        true, // Devanagari
        false, // Ethiopic
        false, // Georgian
        false, // Glagolitic
        false, // Gothic
        false, // Greek
        true, // Gujarati
        true, // Gurmukhi
        true, // Hangul
        false, // Hanunoo
        true, // Hebrew
        true, // Kannada
        false, // Kana
        true, // Kharoshthi
        true, // Khmer
        true, // Lao
        false, // Latin
        true, // Limbu
        false, // LinearB
        true, // Malayalam
        false, // MathematicalAlphanumericSymbols
        true, // Mongolian
        false, // MusicalSymbols
        true, // Myanmar
        true, // NewTaiLue
        false, // Ogham
        false, // OldItalic
        false, // OldPersianCuneiform
        true, // Oriya
        false, // Osmanya
        false, // Runic
        false, // Shavian
        true, // Sinhala
        true, // SylotiNagri
        false, // Syriac
        false, // Tagalog
        false, // Tagbanwa
        false, // TaiLe
        true, // Tamil
        true, // Telugu
        true, // Thaana
        true, // Thai
        true, // Tibetan
        false, // Tifinagh
        false, // UgariticCuneiform
        false, // Yi
        false, // Digit
        false, // Control
        false, // Mirror
    ];

    private static readonly Lazy<ClassificationNativeTables> Native = new(BuildNative, isThreadSafe: true);

    /// <summary>
    /// Process-lifetime native pointers for <c>MS.Internal.Classification</c>.
    /// Built once; never disposed.
    /// </summary>
    public static ClassificationNativeTables NativeTables => Native.Value;

    /// <summary>
    /// Generates the classification tables for every Unicode scalar value. Class IDs are
    /// equivalence-class indices of identical packed rows, assigned in scalar order.
    /// </summary>
    public static ClassificationTables Build()
    {
        Dictionary<CharacterAttributeRow, ushort> classByRow = [];
        List<CharacterAttributeRow> attributes = [];
        short[][][] planes = new short[17][][];

        for (int plane = 0; plane < 17; plane++)
        {
            short[][] pages = new short[256][];
            for (int page = 0; page < 256; page++)
            {
                short[] cells = new short[256];
                for (int cell = 0; cell < 256; cell++)
                {
                    int scalar = (plane << 16) | (page << 8) | cell;
                    CharacterAttributeRow row = Classify(scalar);
                    if (!classByRow.TryGetValue(row, out ushort classId))
                    {
                        if (attributes.Count >= (int)UnicodeClass.Max)
                        {
                            throw new InvalidOperationException(
                                $"classification exceeds the WPF limit: {attributes.Count + 1} distinct rows > 0x{(int)UnicodeClass.Max:X}.");
                        }

                        classId = (ushort)attributes.Count;
                        classByRow.Add(row, classId);
                        attributes.Add(row);
                    }

                    cells[cell] = (short)classId;
                }

                // Sentinel page: a page whose 256 cells share one class is stored as a 1-element
                // array whose value IS the class ID (WPF sentinel semantics).
                bool uniform = true;
                for (int cell = 1; cell < 256; cell++)
                {
                    if (cells[cell] != cells[0])
                    {
                        uniform = false;
                        break;
                    }
                }

                pages[page] = uniform ? [cells[0]] : cells;
            }

            planes[plane] = pages;
        }

        return new ClassificationTables(planes, [.. attributes]);
    }

    private static ClassificationNativeTables BuildNative()
    {
        return Build().PinNative();
    }

    private static CharacterAttributeRow Classify(int scalar)
    {
        sbyte gc = UCharType(scalar);
        int direction = UCharDirection(scalar);

        ScriptID scriptId = ClassifyScript(scalar, gc);
        ItemClass itemClass = ClassifyItemClass(scalar, gc, direction, scriptId);
        ushort flags = ClassifyFlags(scalar, gc, direction, scriptId, itemClass);

        return new CharacterAttributeRow((byte)scriptId, (byte)itemClass, flags, 0, DirectionMap[direction], 0);
    }

    private static ScriptID ClassifyScript(int scalar, sbyte gc)
    {
        if (IsAsciiDigit(scalar))
        {
            return ScriptID.Digit;
        }

        if (gc == UDecimalDigitNumber)
        {
            int script = GetScript(scalar);
            if (script is UScriptCommon or UScriptInherited)
            {
                return ScriptID.Digit;
            }
        }

        if (gc is UControlChar or UFormatChar)
        {
            return ScriptID.Control;
        }

        // Blocks WPF names as scripts (OT-tag order has no ICU script counterpart).
        if (scalar is >= 0x1D100 and <= 0x1D1FF)
        {
            return ScriptID.MusicalSymbols;
        }

        if (scalar is >= 0x1D400 and <= 0x1D7FF)
        {
            return ScriptID.MathematicalAlphanumericSymbols;
        }

        int rawScript = GetScript(scalar);
        return rawScript == UScriptCommon && UIsMirrored(scalar) != 0 ? ScriptID.Mirror : MapScript(rawScript);
    }

    private static ItemClass ClassifyItemClass(int scalar, sbyte gc, int direction, ScriptID scriptId)
    {
        return IsAsciiDigit(scalar) || scriptId == ScriptID.Digit
            ? ItemClass.DigitClass
            : direction switch
            {
                UArabicNumber => ItemClass.ANClass,
                UCommonNumberSeparator => ItemClass.CSClass,
                UEuropeanNumberSeparator when gc == UOtherPunctuation => ItemClass.NumberSignClass,
                UEuropeanNumberTerminator when gc == UOtherPunctuation => ItemClass.NumberSignClass,
                UEuropeanNumberSeparator => ItemClass.ESClass,
                UEuropeanNumberTerminator => ItemClass.ETClass,
                _ when gc is UNonSpacingMark or UEnclosingMark or UCombiningSpacingMark => ScriptCaretInfo[(byte)scriptId]
                    ? ItemClass.ComplexMarkClass
                    : ItemClass.SimpleMarkClass,
                _ when scalar is 0x200C or 0x200D => ItemClass.JoinerClass,
                ULeftToRight or URightToLeft or URightToLeftArabic => ItemClass.StrongClass,
                _ when gc is UControlChar or UFormatChar => ItemClass.ControlClass,
                _ => ItemClass.WeakClass,
            };
    }

    private static ushort ClassifyFlags(int scalar, sbyte gc, int direction, ScriptID scriptId, ItemClass itemClass)
    {
        CharacterAttributeFlag flags = 0;

        bool isRtl = direction is URightToLeft or URightToLeftArabic or URightToLeftEmbedding or URightToLeftOverride or URightToLeftIsolate;
        bool isMark = gc is UNonSpacingMark or UEnclosingMark or UCombiningSpacingMark;
        bool isComplexScript = scriptId is ScriptID.Bengali
            or ScriptID.Devanagari
            or ScriptID.Gurmukhi
            or ScriptID.Gujarati
            or ScriptID.Kannada
            or ScriptID.Malayalam
            or ScriptID.Oriya
            or ScriptID.Tamil
            or ScriptID.Telugu
            or ScriptID.Thai
            or ScriptID.Lao
            or ScriptID.Khmer
            or ScriptID.Myanmar;
        bool isJamo = scalar is (>= 0x1100 and <= 0x11FF) or (>= 0xA960 and <= 0xA97F) or (>= 0xD7B0 and <= 0xD7FF);
        bool isSurrogate = scalar is >= 0xD800 and <= 0xDFFF;
        bool isFormatControl = gc == UFormatChar;

        if (isMark || isRtl || isComplexScript || isJamo || isSurrogate || isFormatControl)
        {
            flags |= CharacterAttributeFlag.CharacterComplex;
        }

        if (isRtl)
        {
            flags |= CharacterAttributeFlag.CharacterRTL;
        }

        if (scalar is 0x000A or 0x000B or 0x000C or 0x0085 or 0x2028)
        {
            flags |= CharacterAttributeFlag.CharacterLineBreak;
        }

        if (scalar is 0x000D or 0x2029)
        {
            flags |= CharacterAttributeFlag.CharacterParaBreak;
        }

        if (scalar is 0x000A or 0x000D)
        {
            flags |= CharacterAttributeFlag.CharacterCRLF;
        }

        if (scalar == 0x0009)
        {
            flags |= CharacterAttributeFlag.CharacterFormatAnchor;
        }

        if (gc == USpaceSeparator)
        {
            flags |= CharacterAttributeFlag.CharacterSpace;
        }

        if (scriptId == ScriptID.Digit)
        {
            flags |= CharacterAttributeFlag.CharacterDigit;
        }

        if (gc is >= UUppercaseLetter and <= UOtherLetter)
        {
            flags |= CharacterAttributeFlag.CharacterLetter;
        }

        if (UGetIntPropertyValue(scalar, UcharIdeographic) != 0)
        {
            flags |= CharacterAttributeFlag.CharacterIdeo;
        }

        if (scalar > 0xFFFF)
        {
            flags |= CharacterAttributeFlag.CharacterExtended;
        }

        if (IsFastText(scalar, gc, scriptId, itemClass))
        {
            flags |= CharacterAttributeFlag.CharacterFastText;
        }

        return (ushort)flags;
    }

    private static bool IsFastText(int scalar, sbyte gc, ScriptID scriptId, ItemClass itemClass)
    {
        if (scalar > 0xFFFF || scriptId is not (ScriptID.Latin or ScriptID.Default))
        {
            return false;
        }

        if (itemClass is ItemClass.SimpleMarkClass or ItemClass.ComplexMarkClass or ItemClass.DigitClass)
        {
            return false;
        }

        bool isLetter = gc is >= UUppercaseLetter and <= UOtherLetter;
        bool isPunctuation = gc is UDashPunctuation
            or UStartPunctuation
            or UEndPunctuation
            or UConnectorPunctuation
            or UOtherPunctuation
            or UInitialPunctuation
            or UFinalPunctuation;
        bool isSpace = gc == USpaceSeparator;
        return isLetter || isPunctuation || isSpace;
    }

    private static bool IsAsciiDigit(int scalar)
    {
        return scalar is >= '0' and <= '9';
    }

    private static ScriptID MapScript(int script)
    {
        return script switch
        {
            UScriptArabic => ScriptID.Arabic,
            UScriptArmenian => ScriptID.Armenian,
            UScriptBengali => ScriptID.Bengali,
            UScriptBopomofo => ScriptID.Bopomofo,
            UScriptBraille => ScriptID.Braille,
            UScriptBuginese => ScriptID.Buginese,
            UScriptBuhid => ScriptID.Buhid,
            UScriptCanadianAboriginal => ScriptID.CanadianSyllabics,
            UScriptCherokee => ScriptID.Cherokee,
            UScriptCoptic => ScriptID.Coptic,
            UScriptCypriot => ScriptID.CypriotSyllabary,
            UScriptCyrillic => ScriptID.Cyrillic,
            UScriptDeseret => ScriptID.Deseret,
            UScriptDevanagari => ScriptID.Devanagari,
            UScriptEthiopic => ScriptID.Ethiopic,
            UScriptGeorgian => ScriptID.Georgian,
            UScriptGlagolitic => ScriptID.Glagolitic,
            UScriptGothic => ScriptID.Gothic,
            UScriptGreek => ScriptID.Greek,
            UScriptGujarati => ScriptID.Gujarati,
            UScriptGurmukhi => ScriptID.Gurmukhi,
            UScriptHan => ScriptID.CJKIdeographic,
            UScriptHangul => ScriptID.Hangul,
            UScriptHanunoo => ScriptID.Hanunoo,
            UScriptHebrew => ScriptID.Hebrew,
            UScriptHiragana => ScriptID.Kana,
            UScriptKannada => ScriptID.Kannada,
            UScriptKatakana or UScriptKatakanaOrHiragana => ScriptID.Kana,
            UScriptKharoshthi => ScriptID.Kharoshthi,
            UScriptKhmer => ScriptID.Khmer,
            UScriptLao => ScriptID.Lao,
            UScriptLatin => ScriptID.Latin,
            UScriptLimbu => ScriptID.Limbu,
            UScriptLinearB => ScriptID.LinearB,
            UScriptMalayalam => ScriptID.Malayalam,
            UScriptMongolian => ScriptID.Mongolian,
            UScriptMyanmar => ScriptID.Myanmar,
            UScriptNewTaiLue => ScriptID.NewTaiLue,
            UScriptOgham => ScriptID.Ogham,
            UScriptOldItalic => ScriptID.OldItalic,
            UScriptOldPersian => ScriptID.OldPersianCuneiform,
            UScriptOriya => ScriptID.Oriya,
            UScriptOsmanya => ScriptID.Osmanya,
            UScriptRunic => ScriptID.Runic,
            UScriptShavian => ScriptID.Shavian,
            UScriptSinhala => ScriptID.Sinhala,
            UScriptSylotiNagri => ScriptID.SylotiNagri,
            UScriptSyriac => ScriptID.Syriac,
            UScriptTagalog => ScriptID.Tagalog,
            UScriptTagbanwa => ScriptID.Tagbanwa,
            UScriptTaiLe => ScriptID.TaiLe,
            UScriptTamil => ScriptID.Tamil,
            UScriptTelugu => ScriptID.Telugu,
            UScriptThaana => ScriptID.Thaana,
            UScriptThai => ScriptID.Thai,
            UScriptTibetan => ScriptID.Tibetan,
            UScriptTifinagh => ScriptID.Tifinagh,
            UScriptUgaritic => ScriptID.UgariticCuneiform,
            UScriptYi => ScriptID.Yi,
            _ => ScriptID.Default,
        };
    }

    private static int GetScript(int scalar)
    {
        return UGetIntPropertyValue(scalar, UcharScript);
    }

    [LibraryImport(NativeLibrary, EntryPoint = "u_charType_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial sbyte UCharType(int scalar);

    [LibraryImport(NativeLibrary, EntryPoint = "u_charDirection_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UCharDirection(int scalar);

    [LibraryImport(NativeLibrary, EntryPoint = "u_isMirrored_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial sbyte UIsMirrored(int scalar);

    [LibraryImport(NativeLibrary, EntryPoint = "u_getIntPropertyValue_78")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UGetIntPropertyValue(int scalar, int property);
}
