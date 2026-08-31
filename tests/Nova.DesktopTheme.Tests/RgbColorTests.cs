namespace Nova.DesktopTheme.Tests;

public sealed class RgbColorTests
{
    [Fact]
    public void TryParseCsv_ValidTriplet_Parses()
    {
        Assert.True(RgbColor.TryParseCsv("10,160,230", out RgbColor color));
        Assert.Equal(new RgbColor(10, 160, 230), color);
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("a,b,c")]
    [InlineData("999,0,0")]
    [InlineData("1,2,3,4")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseCsv_Malformed_ReturnsFalse(string? text)
    {
        Assert.False(RgbColor.TryParseCsv(text, out _));
    }

    [Fact]
    public void TryParseHex_WithAndWithoutHash_AreEqual()
    {
        Assert.True(RgbColor.TryParseHex("#0AA0E6", out RgbColor withHash));
        Assert.True(RgbColor.TryParseHex("0aa0e6", out RgbColor withoutHash));
        Assert.Equal(withHash, withoutHash);
        Assert.Equal(new RgbColor(10, 160, 230), withHash);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("xyz")]
    [InlineData("#gggggg")]
    [InlineData("#1234567")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseHex_Malformed_ReturnsFalse(string? text)
    {
        Assert.False(RgbColor.TryParseHex(text, out _));
    }

    [Fact]
    public void ToColorRef_UsesBgrLayout()
    {
        Assert.Equal(0x00E6A00A, new RgbColor(10, 160, 230).ToColorRef());
        Assert.Equal(0x001E1E1E, new RgbColor(30, 30, 30).ToColorRef());
        Assert.Equal(0x00444444, new RgbColor(68, 68, 68).ToColorRef());
    }

    [Fact]
    public void FromColorRef_RoundTrips()
    {
        RgbColor original = new(10, 160, 230);
        Assert.Equal(original, RgbColor.FromColorRef(original.ToColorRef()));
    }

    [Fact]
    public void Lighten_BlendsTowardWhite()
    {
        RgbColor result = new RgbColor(0, 0, 0).Lighten(0.5);
        Assert.Equal(new RgbColor(128, 128, 128), result);
    }

    [Fact]
    public void Darken_BlendsTowardBlack()
    {
        RgbColor result = new RgbColor(255, 255, 255).Darken(0.5);
        Assert.Equal(new RgbColor(128, 128, 128), result);
    }

    [Fact]
    public void Equality_IsComponentWise()
    {
        Assert.Equal(new RgbColor(1, 2, 3), new RgbColor(1, 2, 3));
        Assert.NotEqual(new RgbColor(1, 2, 3), new RgbColor(1, 2, 4));
        Assert.True(new RgbColor(1, 2, 3) == new RgbColor(1, 2, 3));
        Assert.True(new RgbColor(1, 2, 3) != new RgbColor(1, 2, 4));
    }

    [Fact]
    public void ToString_IsUpperHex()
    {
        Assert.Equal("#0AA0E6", new RgbColor(10, 160, 230).ToString());
    }
}
