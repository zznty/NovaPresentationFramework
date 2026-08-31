namespace Nova.DesktopTheme.Tests;

public sealed class IniFileTests
{
    [Fact]
    public void Parse_SectionKeyValue_ReadsValue()
    {
        IniFile ini = IniFile.Parse("[Colors:Button]\nBackgroundNormal=68,68,68\n");
        Assert.Equal("68,68,68", ini["Colors:Button", "BackgroundNormal"]);
    }

    [Fact]
    public void Parse_Lookup_IsCaseInsensitive()
    {
        IniFile ini = IniFile.Parse("[Colors:Window]\nBackgroundNormal=30,30,30\n");
        Assert.Equal("30,30,30", ini["colors:window", "backgroundnormal"]);
    }

    [Fact]
    public void Parse_QuotedValue_Unquotes()
    {
        IniFile ini = IniFile.Parse("[qt]\nfont=\"Noto Sans,10,-1,0,400\"\n");
        Assert.Equal("Noto Sans,10,-1,0,400", ini["qt", "font"]);
    }

    [Fact]
    public void Parse_KdeSemicolonEscape_Unescapes()
    {
        IniFile ini = IniFile.Parse("[qt]\nvalue=a\\;b\n");
        Assert.Equal("a;b", ini["qt", "value"]);
    }

    [Fact]
    public void Parse_KeyWithBackslash_IsLiteral()
    {
        IniFile ini = IniFile.Parse("[qt]\nPalette\\active=#dedede\n");
        Assert.Equal("#dedede", ini["qt", "Palette\\active"]);
    }

    [Fact]
    public void Parse_BomAndCrlf_AreTolerated()
    {
        string bom = "\uFEFF";
        IniFile ini = IniFile.Parse(bom + "[A]\r\nKey=Value\r\n");
        Assert.Equal("Value", ini["A", "Key"]);
    }

    [Fact]
    public void Parse_CommentsAndBlankLines_AreSkipped()
    {
        IniFile ini = IniFile.Parse("# comment\n; other\n\n[A]\nKey=Value\n");
        Assert.Equal("Value", ini["A", "Key"]);
    }

    [Fact]
    public void Parse_MalformedLines_AreSkipped()
    {
        IniFile ini = IniFile.Parse("[A]\n=novalue\nnovalue\n[broken\nKey=Value\n");
        Assert.Equal("Value", ini["A", "Key"]);
        Assert.Null(ini["A", ""]);
    }

    [Fact]
    public void Parse_MissingKey_ReturnsNull()
    {
        IniFile ini = IniFile.Parse("[A]\nKey=Value\n");
        Assert.Null(ini["A", "Missing"]);
        Assert.Null(ini["Missing", "Key"]);
    }

    [Fact]
    public void Parse_DuplicateKey_LastWins()
    {
        IniFile ini = IniFile.Parse("[A]\nKey=First\nKey=Second\n");
        Assert.Equal("Second", ini["A", "Key"]);
    }

    [Fact]
    public void Parse_EmptyText_IsEmpty()
    {
        IniFile ini = IniFile.Parse(string.Empty);
        Assert.Null(ini["A", "Key"]);
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNull()
    {
        _ = Assert.Throws<ArgumentNullException>(() => IniFile.Parse(null!));
    }
}
