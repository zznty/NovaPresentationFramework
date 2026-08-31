namespace Nova.FontConfig.Tests;

public sealed class FontConfigTests
{
    [Fact]
    public void Match_DejaVuSans_ReturnsExistingFile()
    {
        using FontConfigLibrary library = new();
        Assert.True(library.IsInitialized);

        FontMatch match = library.Match(new FontQuery("DejaVu Sans"));
        Assert.True(File.Exists(match.FilePath), $"fontconfig matched '{match.FilePath}' which does not exist.");
        Assert.False(string.IsNullOrWhiteSpace(match.Family));
    }

    [Fact]
    public void Match_SansSerif_ReturnsExistingFile()
    {
        using FontConfigLibrary library = new();
        FontMatch match = library.Match(new FontQuery("sans-serif"));
        Assert.True(File.Exists(match.FilePath), $"fontconfig matched '{match.FilePath}' which does not exist.");
        Assert.False(string.IsNullOrWhiteSpace(match.Family));
        Assert.True(match.FaceIndex >= 0);
        Assert.True(match.Weight > 0);
    }

    [Fact]
    public void ListFamilies_WritesUniqueNames_ReturnsCount()
    {
        using FontConfigLibrary library = new();
        string[] families = new string[64];
        int count = library.ListFamilies(families);
        Assert.InRange(count, 1, families.Length);
        Assert.Equal(count, families.Take(count).Distinct(StringComparer.Ordinal).Count());
        Assert.All(families.Take(count), family => Assert.False(string.IsNullOrWhiteSpace(family)));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        FontConfigLibrary library = new();
        library.Dispose();
        library.Dispose();
        Assert.False(library.IsInitialized);
    }

    [Fact]
    public void Match_AfterDispose_Throws()
    {
        FontConfigLibrary library = new();
        library.Dispose();

        ObjectDisposedException ex = Assert.Throws<ObjectDisposedException>(() => library.Match(new FontQuery("sans-serif")));
        Assert.Equal("Nova.FontConfig.FontConfigLibrary", ex.ObjectName);
    }

    [Fact]
    public void RegisterAppFont_BundledIconFont_ResolvesByRenamedFamily()
    {
        // The Fluent icon font ships as fonts/NovaFluentIcons.ttf in the output. Registering it
        // with fontconfig must make the renamed family ("Nova Fluent Icons", not the generic
        // "Symbols") resolvable — the exact mechanism the Fluent theme's app-level
        // SymbolThemeFontFamily override relies on.
        string fontPath = Path.Combine(AppContext.BaseDirectory, "fonts", "NovaFluentIcons.ttf");
        Assert.True(File.Exists(fontPath), $"bundled icon font missing: {fontPath}");
        FontConfigLibrary.RegisterAppFont(fontPath);

        using FontConfigLibrary library = new();
        FontMatch match = library.Match(new FontQuery("Nova Fluent Icons"));
        Assert.Equal("Nova Fluent Icons", match.Family);
        Assert.True(File.Exists(match.FilePath), $"matched '{match.FilePath}' which does not exist.");
    }

    [Fact]
    public void RegisterAppFont_MissingFile_Throws()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "fonts", "DefinitelyNotThere.ttf");
        FontConfigException ex = Assert.Throws<FontConfigException>(() => FontConfigLibrary.RegisterAppFont(missing));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterAppFont_SurvivesLibraryReconstruction()
    {
        // Each FontConfigLibrary ctor re-runs FcInit, which rebuilds the global config; the
        // registered app font must be re-applied so a later library still resolves it.
        string fontPath = Path.Combine(AppContext.BaseDirectory, "fonts", "NovaFluentIcons.ttf");
        Assert.True(File.Exists(fontPath), $"bundled icon font missing: {fontPath}");
        FontConfigLibrary.RegisterAppFont(fontPath);

        using (FontConfigLibrary first = new())
        {
            FontMatch m1 = first.Match(new FontQuery("Nova Fluent Icons"));
            Assert.Equal("Nova Fluent Icons", m1.Family);
        }

        using FontConfigLibrary second = new();
        FontMatch m2 = second.Match(new FontQuery("Nova Fluent Icons"));
        Assert.Equal("Nova Fluent Icons", m2.Family);
    }
}
