using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Silk.NET.SDL;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.Sdl;

/// <summary>The SDL dialog flavor, mirroring the SDL_Show*FileDialog trio.</summary>
public enum FileDialogKind
{
    Open,
    Save,
    Folder,
}

/// <summary>One file-dialog filter: a user-readable label and an extension pattern.</summary>
/// <param name="Name">User-readable label, e.g. "Text documents".</param>
/// <param name="Pattern">SDL pattern syntax: a semicolon-separated list of extensions
/// ("txt;md"), or "*" for all files. No "*." prefix — SDL rejects it.</param>
public sealed record FileDialogFilter(string Name, string Pattern);

/// <summary>
/// The result channel of an asynchronous SDL file dialog. <see cref="SdlFileDialog.Show"/>
/// starts the dialog and returns immediately; <see cref="Completed"/> fires on the SDL
/// thread (which may or may not be the calling thread) when the user finishes. The caller
/// keeps pumping the event loop until then.
/// </summary>
[PublicAPI]
public sealed class FileDialogSession
{
    internal GCHandle Handle;

    private readonly List<GCHandle> _pins = [];
    private int _nativeFreed;

    /// <summary>Raised when the dialog finishes, on the SDL thread. Never raised more than once.</summary>
    public event EventHandler<FileDialogCompletedEventArgs>? Completed;

    /// <summary>
    /// The chosen paths, in UTF-8 order. <see langword="null"/> = the dialog failed
    /// (SDL_GetError has details); an empty array = the user canceled.
    /// </summary>
    public IReadOnlyList<string>? FileNames { get; private set; }

    /// <summary>The 1-based index of the chosen filter, or 0 when none was reported.</summary>
    public int FilterIndex { get; private set; }

    /// <summary>Pins a null-terminated UTF-8 buffer until the dialog finishes. The SDL contract
    /// requires the filter strings and the default location to outlive the dialog.</summary>
    internal unsafe sbyte* Pin(string? text)
    {
        byte[] utf8 = text is null ? [0] : [.. Encoding.UTF8.GetBytes(text), 0];
        GCHandle pin = GCHandle.Alloc(utf8, GCHandleType.Pinned);
        _pins.Add(pin);
        return (sbyte*)pin.AddrOfPinnedObject();
    }

    internal void Complete(string[]? fileNames, int filterIndex)
    {
        FileNames = fileNames;
        FilterIndex = Math.Max(0, filterIndex);
        FreeNative();
        Completed?.Invoke(this, new FileDialogCompletedEventArgs(this));
    }

    /// <summary>Releases the pinned buffers and the self handle. Idempotent; safe to call from
    /// the dialog callback (the last native use).</summary>
    private void FreeNative()
    {
        if (Interlocked.Exchange(ref _nativeFreed, 1) != 0)
        {
            return;
        }

        foreach (GCHandle pin in _pins)
        {
            pin.Free();
        }

        _pins.Clear();
        if (Handle.IsAllocated)
        {
            Handle.Free();
        }
    }
}

/// <summary>Event args carrying the session that completed. The dialog contract (cancel vs
/// error vs chosen files) is read off the session's <see cref="FileDialogSession.FileNames"/>.</summary>
public sealed class FileDialogCompletedEventArgs(FileDialogSession session) : EventArgs
{
    /// <summary>The session whose dialog finished.</summary>
    public FileDialogSession Session { get; } = session;
}

/// <summary>
/// SDL3 file dialogs. The Linux backend is the XDG Desktop Portal
/// (<c>org.freedesktop.portal.FileChooser</c>) with a zenity fallback — the same native
/// dialogs every other Linux application shows. The SDL API has no title parameter (that
/// needs the properties variant), so the compositor's own dialog title is used. All Silk
/// unsafe calls stay in this assembly.
/// </summary>
[PublicAPI]
public static class SdlFileDialog
{
    /// <summary>
    /// Starts a modal file dialog asynchronously and returns its result channel. The dialog
    /// needs the SDL event loop running to complete — the caller must keep pumping until
    /// <see cref="FileDialogSession.Completed"/> fires. <paramref name="parent"/> may be
    /// <see langword="null"/>.
    /// </summary>
    public static FileDialogSession Show(
        FileDialogKind kind,
        SdlWindow? parent,
        FileDialogFilter[]? filters,
        string? initialDirectory,
        bool allowMany)
    {
        var session = new FileDialogSession();
        session.Handle = GCHandle.Alloc(session);

        unsafe
        {
            void* userdata = (void*)GCHandle.ToIntPtr(session.Handle);
            sbyte* defaultLocation = session.Pin(initialDirectory);
            DialogFileFilter[] nativeFilters = filters is { Length: > 0 }
                ? [.. filters.Select(f => new DialogFileFilter { Name = session.Pin(f.Name), Pattern = session.Pin(f.Pattern) })]
                : [];

            fixed (DialogFileFilter* filtersPtr = nativeFilters)
            {
                Silk.NET.SDL.WindowHandle window = new((void*)(parent?.Handle.Value ?? 0));
                DialogFileCallback callback = new(&HandleDialogResult);
                switch (kind)
                {
                    case FileDialogKind.Open:
                        SdlApi.ShowOpenFileDialog(callback, userdata, window, filtersPtr, nativeFilters.Length, defaultLocation, (byte)(allowMany ? 1 : 0));
                        break;
                    case FileDialogKind.Save:
                        SdlApi.ShowSaveFileDialog(callback, userdata, window, filtersPtr, nativeFilters.Length, defaultLocation);
                        break;
                    case FileDialogKind.Folder:
                        SdlApi.ShowOpenFolderDialog(callback, userdata, window, defaultLocation, (byte)(allowMany ? 1 : 0));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown dialog kind.");
                }
            }
        }

        return session;
    }

    [UnmanagedCallersOnly]
    private static unsafe void HandleDialogResult(void* userdata, sbyte** filelist, int filter)
    {
        var session = (FileDialogSession)GCHandle.FromIntPtr((nint)userdata).Target!;
        if (filelist is null)
        {
            // The SDL contract: a NULL filelist means the dialog errored.
            session.Complete(null, filter);
            return;
        }

        if (filelist[0] is null)
        {
            // A pointer to NULL means the user canceled.
            session.Complete([], filter);
            return;
        }

        var names = new List<string>();
        for (int i = 0; filelist[i] is not null; i++)
        {
            names.Add(Marshal.PtrToStringUTF8((nint)filelist[i]) ?? string.Empty);
        }

        session.Complete([.. names], filter);
    }
}
