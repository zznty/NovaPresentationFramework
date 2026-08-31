using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Classification;

/// <summary>
/// WPF-private bidirectional class numbering (not ICU <c>UCharDirection</c> values).
/// Internal, mirroring the <c>MS.Internal</c> visibility of the WPF originals.
/// </summary>
internal enum DirectionClass : byte
{
    Left = 0,
    Right = 1,
    ArabicNumber = 2,
    EuropeanNumber = 3,
    ArabicLetter = 4,
    EuropeanSeparator = 5,
    CommonSeparator = 6,
    EuropeanTerminator = 7,
    NonSpacingMark = 8,
    BoundaryNeutral = 9,
    GenericNeutral = 10,
    ParagraphSeparator = 11,
    LeftToRightEmbedding = 12,
    LeftToRightOverride = 13,
    RightToLeftEmbedding = 14,
    RightToLeftOverride = 15,
    PopDirectionalFormat = 16,
    SegmentSeparator = 17,
    WhiteSpace = 18,
    OtherNeutral = 19,
    ClassInvalid = 20,
    ClassMax = 21,
}

/// <summary>WPF-private item class numbering.</summary>
internal enum ItemClass : byte
{
    DigitClass = 0x0,
    ANClass = 0x1,
    CSClass = 0x2,
    ESClass = 0x3,
    ETClass = 0x4,
    StrongClass = 0x5,
    WeakClass = 0x6,
    SimpleMarkClass = 0x7,
    ComplexMarkClass = 0x8,
    ControlClass = 0x9,
    JoinerClass = 0xA,
    NumberSignClass = 0xB,
}

/// <summary>
/// WPF script identifier mapped one-to-one to the OpenType published script tag.
/// Internal, mirroring the <c>MS.Internal</c> visibility of the WPF originals.
/// </summary>
internal enum ScriptID : byte
{
    Default = 0x0,
    Arabic = 0x1,
    Armenian = 0x2,
    Bengali = 0x3,
    Bopomofo = 0x4,
    Braille = 0x5,
    Buginese = 0x6,
    Buhid = 0x7,
    CanadianSyllabics = 0x8,
    Cherokee = 0x9,
    CJKIdeographic = 0xA,
    Coptic = 0xB,
    CypriotSyllabary = 0xC,
    Cyrillic = 0xD,
    Deseret = 0xE,
    Devanagari = 0xF,
    Ethiopic = 0x10,
    Georgian = 0x11,
    Glagolitic = 0x12,
    Gothic = 0x13,
    Greek = 0x14,
    Gujarati = 0x15,
    Gurmukhi = 0x16,
    Hangul = 0x17,
    Hanunoo = 0x18,
    Hebrew = 0x19,
    Kannada = 0x1A,
    Kana = 0x1B,
    Kharoshthi = 0x1C,
    Khmer = 0x1D,
    Lao = 0x1E,
    Latin = 0x1F,
    Limbu = 0x20,
    LinearB = 0x21,
    Malayalam = 0x22,
    MathematicalAlphanumericSymbols = 0x23,
    Mongolian = 0x24,
    MusicalSymbols = 0x25,
    Myanmar = 0x26,
    NewTaiLue = 0x27,
    Ogham = 0x28,
    OldItalic = 0x29,
    OldPersianCuneiform = 0x2A,
    Oriya = 0x2B,
    Osmanya = 0x2C,
    Runic = 0x2D,
    Shavian = 0x2E,
    Sinhala = 0x2F,
    SylotiNagri = 0x30,
    Syriac = 0x31,
    Tagalog = 0x32,
    Tagbanwa = 0x33,
    TaiLe = 0x34,
    Tamil = 0x35,
    Telugu = 0x36,
    Thaana = 0x37,
    Thai = 0x38,
    Tibetan = 0x39,
    Tifinagh = 0x3A,
    UgariticCuneiform = 0x3B,
    Yi = 0x3C,
    Digit = 0x3D,
    Control = 0x3E,
    Mirror = 0x3F,
}

/// <summary>WPF character attribute flags.</summary>
[Flags]
internal enum CharacterAttributeFlag : ushort
{
    CharacterComplex = 0x1,
    CharacterRTL = 0x2,
    CharacterLineBreak = 0x4,
    CharacterFormatAnchor = 0x8,
    CharacterFastText = 0x10,
    CharacterIdeo = 0x20,
    CharacterExtended = 0x40,
    CharacterSpace = 0x80,
    CharacterDigit = 0x100,
    CharacterParaBreak = 0x200,
    CharacterCRLF = 0x400,
    CharacterLetter = 0x800,
}

/// <summary>WPF Unicode class limit: generated tables must stay below this many distinct classes.</summary>
internal enum UnicodeClass : ushort
{
    Max = 0x1D8,
}

/// <summary>
/// Packed classification row, layout-compatible with the WPF-private <c>CharacterAttribute</c>
/// (<c>byte Script; byte ItemClass; ushort Flags; byte BreakType; byte BiDi; short LineBreak</c>,
/// <c>Pack = 1</c>). <c>BreakType</c> and <c>LineBreak</c> are dead fields for the managed
/// consumer and are always 0.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[PublicAPI]
public readonly struct CharacterAttributeRow(byte script, byte itemClass, ushort flags, byte breakType, byte biDi, short lineBreak)
    : IEquatable<CharacterAttributeRow>
{
    /// <summary>Script ID.</summary>
    public byte Script { get; } = script;

    /// <summary>Item class.</summary>
    public byte ItemClass { get; } = itemClass;

    /// <summary>Character attribute flags.</summary>
    public ushort Flags { get; } = flags;

    /// <summary>Breaking type; always 0.</summary>
    public byte BreakType { get; } = breakType;

    /// <summary>Directional class.</summary>
    public byte BiDi { get; } = biDi;

    /// <summary>Line break class; always 0.</summary>
    public short LineBreak { get; } = lineBreak;

    /// <inheritdoc />
    public bool Equals(CharacterAttributeRow other)
    {
        return Script == other.Script
            && ItemClass == other.ItemClass
            && Flags == other.Flags
            && BreakType == other.BreakType
            && BiDi == other.BiDi
            && LineBreak == other.LineBreak;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CharacterAttributeRow other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Script, ItemClass, Flags, BreakType, BiDi, LineBreak);
    }

    /// <summary>Tests equality.</summary>
    public static bool operator ==(CharacterAttributeRow left, CharacterAttributeRow right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests inequality.</summary>
    public static bool operator !=(CharacterAttributeRow left, CharacterAttributeRow right)
    {
        return !left.Equals(right);
    }
}
