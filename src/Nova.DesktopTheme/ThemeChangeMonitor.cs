namespace Nova.DesktopTheme;

/// <summary>
/// Watches the desktop-theme sources for changes and raises <see cref="Changed"/> when any
/// of them is rewritten (KDE System Settings "Apply" rewrites kdeglobals/Trolltech.conf;
/// the GTK files mirror the same palette). Watches the CONFIG DIRECTORY and filters by
/// filename because config writers often use atomic rename (write-temp-then-rename), which
/// a file-level watcher misses. Events are debounced so a burst of writes coalesces into one
/// reload. Never throws on watcher errors; a failed watcher simply stops raising.
/// </summary>
public sealed class ThemeChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly HashSet<string> _fileNames;
    private readonly System.Threading.Timer? _debounce;
    private int _disposed;

    public ThemeChangeMonitor(string configDirectory, IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        _fileNames = new HashSet<string>(fileNames, StringComparer.Ordinal);
        _debounce = new System.Threading.Timer(OnDebounce, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            if (!Directory.Exists(configDirectory))
            {
                return;
            }

            _watcher = new FileSystemWatcher(configDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // No watchable directory (headless CI, bare WM) — the monitor is inert.
        }
    }

    /// <summary>Raised (debounced) when any watched source file changes or the portal signal fires.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsWatched(e.FullPath))
        {
            Schedule();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsWatched(e.OldFullPath) || IsWatched(e.FullPath))
        {
            Schedule();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // inotify limits or transient failures: drop the watcher rather than throw.
        try
        {
            _watcher!.EnableRaisingEvents = false;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
        }
    }

    private bool IsWatched(string fullPath)
    {
        return _fileNames.Contains(Path.GetFileName(fullPath));
    }

    private void Schedule()
    {
        if (_disposed != 0)
        {
            return;
        }

        _ = _debounce?.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
    }

    private void OnDebounce(object? state)
    {
        if (_disposed != 0)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
