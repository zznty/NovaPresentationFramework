namespace Nova.Hunspell.Tests;

public sealed class HunspellSpellCheckerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nova-hunspell-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_SpellAndSuggest_WithFixtureDictionary()
    {
        WriteDictionary(out string affix, out string dic);

        using HunspellSpellChecker? dictionary = HunspellSpellChecker.TryLoad(affix, dic);
        Assert.NotNull(dictionary);
        if (dictionary is null)
        {
            return;
        }

        Assert.True(dictionary.Spell("hello"));
        Assert.True(dictionary.Spell("Hello")); // case-insensitive check
        Assert.False(dictionary.Spell("helo"));
        Assert.False(dictionary.Spell("zzzz"));

        IReadOnlyList<string> suggestions = dictionary.Suggest("helo");
        Assert.Contains("hello", suggestions);
    }

    [Fact]
    public void TryLoad_MissingFiles_ReturnsNull()
    {
        using HunspellSpellChecker? dictionary = HunspellSpellChecker.TryLoad(
            Path.Combine(_dir, "missing.aff"),
            Path.Combine(_dir, "missing.dic"));
        Assert.Null(dictionary);
    }

    [Fact]
    public void Suggest_MisspelledWord_ReturnsCorrectionsWithoutCrash()
    {
        WriteDictionary(out string affix, out string dic);
        using HunspellSpellChecker? dictionary = HunspellSpellChecker.TryLoad(affix, dic);
        if (dictionary is null)
        {
            return; // libhunspell not present on this host
        }

        IReadOnlyList<string> suggestions = dictionary.Suggest("wrold");
        Assert.Contains("world", suggestions);
    }

    private void WriteDictionary(out string affixPath, out string dictionaryPath)
    {
        affixPath = Path.Combine(_dir, "test.aff");
        dictionaryPath = Path.Combine(_dir, "test.dic");
        File.WriteAllText(affixPath, "SET UTF-8\nTRY abcdefghijklmnopqrstuvwxyz\n");
        File.WriteAllText(dictionaryPath, "3\nhello\nworld\ntest\n");
    }
}
