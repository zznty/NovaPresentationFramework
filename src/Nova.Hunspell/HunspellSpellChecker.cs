using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Hunspell;

/// <summary>
/// A loaded Hunspell dictionary (an affix file plus a word-list file), wrapping the
/// system <c>libhunspell-1.7</c>. Hunspell is tri-licensed (LGPL-2.1 / GPL-2 /
/// MPL-1.1); this project consumes it under the MPL option as a dynamically
/// loaded system library — the Firefox model — see NOTICE. Loading fails cleanly
/// (a null return from <see cref="TryLoad"/>) when the library or the dictionary
/// files are missing.
/// </summary>
[PublicAPI]
public sealed partial class HunspellSpellChecker : IDisposable
{
    private const string NativeLibrary = "hunspell-1.7";

    private IntPtr _handle;
    private bool _disposed;

    private HunspellSpellChecker(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Creates a Hunspell instance from the given affix/dictionary file paths, or
    /// returns <see langword="null"/> when the native library is absent or the
    /// dictionary cannot be loaded. Both files must exist.
    /// </summary>
    public static HunspellSpellChecker? TryLoad(string affixPath, string dictionaryPath)
    {
        ArgumentNullException.ThrowIfNull(affixPath);
        ArgumentNullException.ThrowIfNull(dictionaryPath);

        // Hunspell 1.7's create succeeds lazily even when the files are missing
        // (the failure surfaces as a native crash on the first spell) — validate
        // the files up front.
        if (!File.Exists(affixPath) || !File.Exists(dictionaryPath))
        {
            return null;
        }

        try
        {
            IntPtr handle = HunspellCreate(affixPath, dictionaryPath);
            return handle == IntPtr.Zero ? null : new HunspellSpellChecker(handle);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>True when the word is spelled correctly in this dictionary.</summary>
    public bool Spell(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(word);
        return HunspellSpell(_handle, word) != 0;
    }

    /// <summary>The correction suggestions for a misspelled word (empty when none).</summary>
    public IReadOnlyList<string> Suggest(string word)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(word);

        var suggestions = new List<string>();
        int count = HunspellSuggest(_handle, out IntPtr list, word);
        if (count > 0 && list != IntPtr.Zero)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                string? text = Marshal.PtrToStringUTF8(entry);
                if (text is not null)
                {
                    suggestions.Add(text);
                }
            }
        }

        if (list != IntPtr.Zero)
        {
            HunspellFreeList(_handle, ref list, count);
        }

        return suggestions;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            HunspellDestroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [LibraryImport(NativeLibrary, EntryPoint = "Hunspell_create", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr HunspellCreate(string affixPath, string dictionaryPath);

    [LibraryImport(NativeLibrary, EntryPoint = "Hunspell_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HunspellDestroy(IntPtr handle);

    [LibraryImport(NativeLibrary, EntryPoint = "Hunspell_spell", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int HunspellSpell(IntPtr handle, string word);

    // The native Hunspell_suggest takes char*** — the ADDRESS of a slot the native
    // writes the string-array pointer into; Hunspell_free_list takes the SAME slot
    // address (header-verified: Hunspell_free_list(Hunhandle*, char***, int)) — so
    // the array pointer flows as out IntPtr / ref IntPtr between the two calls.
    [LibraryImport(NativeLibrary, EntryPoint = "Hunspell_suggest", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int HunspellSuggest(IntPtr handle, out IntPtr list, string word);

    [LibraryImport(NativeLibrary, EntryPoint = "Hunspell_free_list")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void HunspellFreeList(IntPtr handle, ref IntPtr list, int count);
}
