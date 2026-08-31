using Nova.SystemTheme;

namespace Nova.DesktopTheme.Tests;

public sealed class TrolltechConfigSourceTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "kde", ".config", "Trolltech.conf");

    [Fact]
    public void Load_Fixture_ProducesBevelsFrameFontAccent()
    {
        var source = new TrolltechConfigSource(FixturePath);
        ThemeData data = source.Load();

        // QPalette roles: 2=Light, 3=Midlight, 4=Dark, 11=Shadow, 21=Accent.
        Assert.Equal(0x002E2E2E, data.Colors[SystemColorIndex.ButtonHighlight].ToColorRef());
        Assert.Equal(0x00272727, data.Colors[SystemColorIndex.ThreeDLight].ToColorRef());
        Assert.Equal(0x00151515, data.Colors[SystemColorIndex.ButtonShadow].ToColorRef());
        Assert.Equal(0x000F0F0F, data.Colors[SystemColorIndex.ThreeDDarkShadow].ToColorRef());
        Assert.Equal(0x001E1E1E, data.Colors[SystemColorIndex.WindowFrame].ToColorRef());
        Assert.Equal(0x00E6A00A, data.AccentColorRef);
        Assert.Equal("Noto Sans", data.FontFamily);
        Assert.Equal(10, data.FontPointSize);
        Assert.Equal(400, data.FontWeight);
    }

    [Fact]
    public void Load_MissingFile_YieldsEmptyData()
    {
        var source = new TrolltechConfigSource("/nonexistent/Trolltech.conf");
        ThemeData data = source.Load();
        Assert.Empty(data.Colors);
        Assert.Null(data.FontFamily);
    }

    [Fact]
    public void Load_MalformedRoleCsv_DropsAllRoles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-detheme-test-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "Trolltech.conf"),
            "[qt]\nPalette\\active=#dedede,#444444,not-a-color,#272727\n");
        try
        {
            var source = new TrolltechConfigSource(Path.Combine(dir, "Trolltech.conf"));
            ThemeData data = source.Load();
            Assert.Empty(data.Colors);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ShortFont_IsIgnored()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-detheme-test-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Trolltech.conf"), "[qt]\nfont=\"Noto\"\n");
        try
        {
            var source = new TrolltechConfigSource(Path.Combine(dir, "Trolltech.conf"));
            ThemeData data = source.Load();
            Assert.Null(data.FontFamily);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
