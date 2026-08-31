using JetBrains.Annotations;

namespace Nova.SdlSource;

/// <summary>The dialog flavor. Values mirror <c>Nova.Sdl.FileDialogKind</c> so the cast is a
/// plain numeric conversion. Declared here so PresentationFramework (which cannot reference
/// Nova.Sdl under Arcade's disabled transitive project refs) can drive the dialog.</summary>
public enum FileDialogKind
{
    Open = 0,
    Save = 1,
    Folder = 2,
}

/// <summary>One file-dialog filter: a user-readable label and an SDL pattern
/// (a semicolon-separated extension list like "txt;md", or "*").</summary>
/// <param name="Name">User-readable label, e.g. "Text documents".</param>
/// <param name="Pattern">SDL pattern syntax; no "*." prefix.</param>
[PublicAPI]
public sealed record FileDialogFilter(string Name, string Pattern);

/// <summary>Event args carrying the session whose dialog finished; the cancel/error/chosen
/// semantics are read off <see cref="FileDialogSession.FileNames"/>.</summary>
public sealed class FileDialogCompletedEventArgs(FileDialogSession session) : EventArgs
{
    /// <summary>The session whose dialog finished.</summary>
    public FileDialogSession Session { get; } = session;
}

/// <summary>
/// The result channel of an SDL file dialog started via
/// <see cref="SdlPresentationSource.ShowFileDialog"/>. <see cref="Completed"/> fires on
/// the SDL thread; the result semantics follow the SDL contract: <see cref="FileNames"/>
/// <see langword="null"/> = the dialog failed, an empty list = the user canceled.
/// </summary>
[PublicAPI]
public sealed class FileDialogSession(Nova.Sdl.FileDialogSession inner)
{
    private readonly Nova.Sdl.FileDialogSession _inner = inner;

    /// <summary>Raised when the dialog finishes, on the SDL thread. Never raised more than once.</summary>
    public event EventHandler<FileDialogCompletedEventArgs>? Completed;

    /// <summary>The chosen paths: <see langword="null"/> = error, empty = canceled.</summary>
    public IReadOnlyList<string>? FileNames => _inner.FileNames;

    /// <summary>The 1-based index of the chosen filter, or 0 when none was reported.</summary>
    public int FilterIndex => _inner.FilterIndex;

    internal void RaiseCompleted()
    {
        Completed?.Invoke(this, new FileDialogCompletedEventArgs(this));
    }
}
