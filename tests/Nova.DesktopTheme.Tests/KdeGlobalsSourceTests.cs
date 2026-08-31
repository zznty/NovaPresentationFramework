using Nova.SystemTheme;

namespace Nova.DesktopTheme.Tests;

public sealed class KdeGlobalsSourceTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "kde", ".config", "kdeglobals");

    [Fact]
    public void Load_MeasuredFixture_ProducesCanonicalPalette()
    {
        var source = new KdeGlobalsSource(FixturePath);
        ThemeData data = source.Load();

        Assert.Equal(0x001E1E1E, data.Colors[SystemColorIndex.Window].ToColorRef());       // 30,30,30
        Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.WindowText].ToColorRef());   // 222,222,222
        Assert.Equal(0x00444444, data.Colors[SystemColorIndex.ButtonFace].ToColorRef());   // 68,68,68
        Assert.Equal(0x00FAFAFA, data.Colors[SystemColorIndex.ButtonText].ToColorRef());   // 250,250,250
        Assert.Equal(0x00E6A00A, data.Colors[SystemColorIndex.Highlight].ToColorRef());    // 10,160,230
        Assert.Equal(0x00FFFFFF, data.Colors[SystemColorIndex.HighlightText].ToColorRef());
        Assert.Equal(0x00141414, data.Colors[SystemColorIndex.Menu].ToColorRef());         // 20,20,20
        Assert.Equal(0x00EAEAEA, data.Colors[SystemColorIndex.MenuText].ToColorRef());     // 234,234,234
        Assert.Equal(0x00323232, data.Colors[SystemColorIndex.Info].ToColorRef());         // 50,50,50
        Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.InfoText].ToColorRef());
        Assert.Equal(0x00333333, data.Colors[SystemColorIndex.ActiveCaption].ToColorRef());    // WM activeBackground
        Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.CaptionText].ToColorRef());      // WM activeForeground
        Assert.Equal(0x00424242, data.Colors[SystemColorIndex.InactiveCaption].ToColorRef());  // WM inactiveBackground
        Assert.Equal(0x00EAEAEA, data.Colors[SystemColorIndex.InactiveCaptionText].ToColorRef());
        Assert.Equal(0x00888888, data.Colors[SystemColorIndex.GrayText].ToColorRef());     // 136,136,136 ForegroundInactive
        Assert.Equal(0x00E6A00A, data.AccentColorRef);
    }

    [Fact]
    public void Load_MissingFile_YieldsEmptyData()
    {
        var source = new KdeGlobalsSource("/nonexistent/kdeglobals");
        ThemeData data = source.Load();
        Assert.Empty(data.Colors);
        Assert.Null(data.AccentColorRef);
        Assert.Null(data.FontFamily);
    }

    [Fact]
    public void Load_MalformedTriplet_IsSkippedPerSlot()
    {
        string dir = CreateTempFile("[Colors:Window]\nBackgroundNormal=not-a-color\nForegroundNormal=222,222,222\n");
        try
        {
            var source = new KdeGlobalsSource(Path.Combine(dir, "kdeglobals"));
            ThemeData data = source.Load();
            Assert.False(data.Colors.ContainsKey(SystemColorIndex.Window));
            Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.WindowText].ToColorRef());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    internal static string CreateTempFile(string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-detheme-test-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "kdeglobals"), content);
        return dir;
    }
}
