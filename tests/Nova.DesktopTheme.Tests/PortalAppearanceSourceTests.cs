namespace Nova.DesktopTheme.Tests;

public sealed class PortalAppearanceSourceTests
{
    [Fact]
    public void Load_DarkScheme_SetsIsDark()
    {
        var source = new PortalAppearanceSource(static (_, key) => key == "color-scheme" ? (uint)1 : null);
        ThemeData data = source.Load();
        Assert.True(data.IsDark);
    }

    [Fact]
    public void Load_LightScheme_SetsIsDarkFalse()
    {
        var source = new PortalAppearanceSource(static (_, key) => key == "color-scheme" ? (uint)0 : null);
        ThemeData data = source.Load();
        Assert.False(data.IsDark);
    }

    [Fact]
    public void Load_AccentFloatRgb_ConvertsToColorRef()
    {
        // (0.0392, 0.6275, 0.9020) → (10, 160, 230) → 0x00E6A00A.
        var source = new PortalAppearanceSource(
            static (_, key) => key == "accent-color" ? (0.039215687662363052, 0.62745100259780884, 0.90196079015731812) : null);
        ThemeData data = source.Load();
        Assert.Equal(0x00E6A00A, data.AccentColorRef);
    }

    [Fact]
    public void Load_ThrowingDelegate_IsSwallowed()
    {
        var source = new PortalAppearanceSource(static (_, _) => throw new InvalidOperationException("no portal"));
        ThemeData data = source.Load();
        Assert.Null(data.IsDark);
        Assert.Null(data.AccentColorRef);
    }

    [Fact]
    public void Load_WrongTypes_AreIgnored()
    {
        var source = new PortalAppearanceSource(static (_, key) => key == "color-scheme" ? "dark" : new object());
        ThemeData data = source.Load();
        Assert.Null(data.IsDark);
        Assert.Null(data.AccentColorRef);
    }

    [Fact]
    public void Load_NullPortal_IsIgnored()
    {
        var source = new PortalAppearanceSource(static (_, _) => null);
        ThemeData data = source.Load();
        Assert.Null(data.IsDark);
        Assert.Null(data.AccentColorRef);
    }
}
