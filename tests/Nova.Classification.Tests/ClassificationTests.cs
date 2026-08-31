namespace Nova.Classification.Tests;

public sealed class ClassificationTests
{
    private static readonly ClassificationTables Tables = ClassificationTableBuilder.Build();

    [Fact]
    public void LatinA_IsLatinStrongLeftLetterFastText_NotComplex()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('A');

        Assert.Equal((byte)ScriptID.Latin, attr.Script);
        Assert.Equal((byte)ItemClass.StrongClass, attr.ItemClass);
        Assert.Equal((byte)DirectionClass.Left, attr.BiDi);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterLetter);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterFastText);
        Assert.Equal(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterComplex);
    }

    [Fact]
    public void DigitZero_IsDigitScriptDigitClassCharacterDigit()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('0');

        Assert.Equal((byte)ScriptID.Digit, attr.Script);
        Assert.Equal((byte)ItemClass.DigitClass, attr.ItemClass);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterDigit);
    }

    [Fact]
    public void Space_HasCharacterSpace()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(' ');

        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterSpace);
    }

    [Fact]
    public void LineFeed_HasLineBreakAndCrLf()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('\n');

        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterLineBreak);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterCRLF);
    }

    [Fact]
    public void CarriageReturn_HasParaBreakAndCrLf()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('\r');

        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterParaBreak);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterCRLF);
    }

    [Fact]
    public void Tab_HasFormatAnchor()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('\t');

        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterFormatAnchor);
    }

    [Fact]
    public void ArabicAlef_IsArabicRtlComplex()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(0x0627);

        Assert.Equal((byte)ScriptID.Arabic, attr.Script);
        Assert.Equal((byte)ItemClass.StrongClass, attr.ItemClass);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterRTL);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterComplex);
    }

    [Fact]
    public void DevanagariKa_IsDevanagariIndicCaretPath()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(0x0915);

        Assert.Equal((byte)ScriptID.Devanagari, attr.Script);
        Assert.NotEqual((byte)ScriptID.Latin, attr.Script);
        Assert.NotEqual(0, attr.Flags & (ushort)CharacterAttributeFlag.CharacterComplex);
    }

    [Fact]
    public void CombiningAcute_IsCombiningMark()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(0x0301);

        Assert.True(
            attr.ItemClass is (byte)ItemClass.SimpleMarkClass or (byte)ItemClass.ComplexMarkClass,
            $"unexpected ItemClass 0x{attr.ItemClass:X}");
    }

    [Fact]
    public void Zwj_IsJoinerClass()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(0x200D);

        Assert.Equal((byte)ItemClass.JoinerClass, attr.ItemClass);
    }

    [Fact]
    public void DigitBidi_EnMapsToWpfEuropeanNumberNotArabicNumber()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar('1');

        Assert.Equal((byte)DirectionClass.EuropeanNumber, attr.BiDi);
        Assert.NotEqual((byte)DirectionClass.ArabicNumber, attr.BiDi);
    }

    [Fact]
    public void ArabicLetterBidi_AlMapsToWpfArabicLetterNotIcuValue()
    {
        CharacterAttributeRow attr = Tables.AttributeOfScalar(0x0627);

        Assert.Equal((byte)DirectionClass.ArabicLetter, attr.BiDi);
        Assert.NotEqual((byte)13, attr.BiDi);
    }

    [Fact]
    public void ClassCount_DoesNotExceedWpfLimit()
    {
        Assert.InRange(Tables.ClassCount, 1, (int)UnicodeClass.Max);
    }

    [Fact]
    public void ClassOf_IsStableAcrossIdenticalRows()
    {
        Assert.Equal(Tables.ClassOf('A'), Tables.ClassOf('a'));
        Assert.Equal(Tables.ClassOf('0'), Tables.ClassOf('9'));
        Assert.Equal(Tables.ClassOf(' '), Tables.ClassOf(0x3000));
    }

    [Fact]
    public void AttributeOf_MatchesAttributeOfScalar()
    {
        ushort classId = Tables.ClassOf('A');
        CharacterAttributeRow fromClass = Tables.AttributeOf(classId);
        CharacterAttributeRow fromScalar = Tables.AttributeOfScalar('A');

        Assert.Equal(fromClass, fromScalar);
    }

    [Fact]
    public void EmojiAndSurrogate_DoNotThrowAndStayInRange()
    {
        ushort emojiClass = Tables.ClassOf(0x1F600);
        Assert.InRange(emojiClass, (ushort)0, (ushort)(UnicodeClass.Max - 1));
        Assert.NotEqual(0, Tables.AttributeOf(emojiClass).Flags & (ushort)CharacterAttributeFlag.CharacterExtended);

        ushort surrogateClass = Tables.ClassOf(0xD800);
        Assert.InRange(surrogateClass, (ushort)0, (ushort)(UnicodeClass.Max - 1));
    }

    [Fact]
    public void PinNative_MatchesManagedLookup()
    {
        using ClassificationNativeTables native = Tables.PinNative();

        foreach (int scalar in new[] { 'A', '0', ' ', '\n', '\r', '\t', 0x0627, 0x0915, 0x0301, 0x200D, 0x1F600, 0xD800 })
        {
            Assert.Equal(Tables.ClassOf(scalar), native.ClassOf(scalar));
            Assert.Equal(Tables.AttributeOfScalar(scalar), native.AttributeOf(Tables.ClassOf(scalar)));
        }
    }

    [Fact]
    public void DeadFields_AreAlwaysZero()
    {
        foreach (int scalar in new[] { 'A', '0', ' ', '\n', '\t', 0x0627, 0x0915, 0x0301, 0x1F600 })
        {
            CharacterAttributeRow attr = Tables.AttributeOfScalar(scalar);
            Assert.Equal(0, attr.BreakType);
            Assert.Equal(0, attr.LineBreak);
        }
    }
}
