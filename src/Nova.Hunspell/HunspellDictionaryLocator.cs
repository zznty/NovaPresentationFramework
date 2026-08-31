using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JetBrains.Annotations;

namespace Nova.Hunspell;

/// <summary>
/// Locates the system's Hunspell dictionary files for a culture name. Searches the
/// conventional locations (<c>/usr/share/hunspell</c>, <c>/usr/share/myspell/dicts</c>,
/// <c>/usr/share/myspell</c>, <c>/usr/local/share/hunspell</c>,
/// <c>~/.local/share/hunspell</c>) and falls back from the full culture name
/// (<c>en_US</c>) to its parent (<c>en</c>), trying common case variants.
/// </summary>
[PublicAPI]
public static class HunspellDictionaryLocator
{
    public static bool TryLocate(string cultureName, [NotNullWhen(true)] out string? affixPath, [NotNullWhen(true)] out string? dictionaryPath)
    {
        ArgumentNullException.ThrowIfNull(cultureName);

        var searchRoots = new List<string>();
        AddIfExists(searchRoots, "/usr/share/hunspell");
        AddIfExists(searchRoots, "/usr/share/myspell/dicts");
        AddIfExists(searchRoots, "/usr/share/myspell");
        AddIfExists(searchRoots, "/usr/local/share/hunspell");
        AddIfExists(searchRoots, "~/.local/share/hunspell");

        // Culture hierarchy: en-US -> en -> the bare name without the region.
        var names = new List<string>();
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            AddName(names, culture.Name);
            AddName(names, culture.TwoLetterISOLanguageName);
            string first = culture.Name.Split('-')[0];
            AddName(names, first);
        }
        catch (CultureNotFoundException)
        {
            AddName(names, cultureName);
        }

        foreach (string root in searchRoots)
        {
            foreach (string name in names)
            {
                if (TryFind(root, name, out affixPath, out dictionaryPath))
                {
                    return true;
                }
            }
        }

        affixPath = null;
        dictionaryPath = null;
        return false;
    }

    private static void AddName(List<string> names, string name)
    {
        if (!string.IsNullOrEmpty(name) && !names.Contains(name))
        {
            names.Add(name);
        }
    }

    private static void AddIfExists(List<string> roots, string path)
    {
        string expanded = path.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/'))
            : path;
        if (Directory.Exists(expanded))
        {
            roots.Add(expanded);
        }
    }

    private static bool TryFind(string root, string name, [NotNullWhen(true)] out string? affixPath, [NotNullWhen(true)] out string? dictionaryPath)
    {
        // Common spellings: en_US (Debian/Ubuntu), en-us, en_US-utf8 style bases
        // are matched by the prefix check below.
        foreach (string candidate in NameCandidates(name))
        {
            string affix = Path.Combine(root, candidate + ".aff");
            string dictionary = Path.Combine(root, candidate + ".dic");
            if (File.Exists(affix) && File.Exists(dictionary))
            {
                affixPath = affix;
                dictionaryPath = dictionary;
                return true;
            }
        }

        affixPath = null;
        dictionaryPath = null;
        return false;
    }

    private static IEnumerable<string> NameCandidates(string name)
    {
        yield return name;
        yield return name.ToUpperInvariant();
        yield return name.Replace('-', '_');
        yield return name.Replace('-', '_').ToUpperInvariant();
    }
}
