using System.Windows.Threading;

namespace Nova.DesktopTheme.Host;

/// <summary>
/// Wires the desktop-theme change sources (file watcher on the config files + the portal
/// <c>SettingChanged</c> signal) to <see cref="DesktopThemeHost.ApplyLive"/>. The apply is
/// marshalled onto the WPF dispatcher because <see cref="System.Windows.SystemColors"/>
/// invalidation and the tree walk must run on the UI thread (mirrors the Windows
/// WM_THEMECHANGED handler). Start once at app startup when the opt-in is on; <see cref="IDisposable"/>
/// stops everything.
/// </summary>
public sealed class ThemeChangeListener : IDisposable
{
    private readonly ThemeChangeMonitor? _fileMonitor;
    private readonly PortalSignalMonitor? _portalMonitor;
    private readonly Dispatcher _dispatcher;
    private int _disposed;

    public ThemeChangeListener(Dispatcher dispatcher, string homeDirectory)
    {
        _dispatcher = dispatcher;
        string configDir = Path.Combine(homeDirectory, ".config");

        _fileMonitor = new ThemeChangeMonitor(
            configDir,
            ["kdeglobals", "Trolltech.conf", "settings.ini", "colors.css"]);
        _fileMonitor.Changed += OnChanged;

        _portalMonitor = new PortalSignalMonitor();
        _portalMonitor.AppearanceChanged += OnChanged;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_fileMonitor is not null)
        {
            _fileMonitor.Changed -= OnChanged;
            _fileMonitor.Dispose();
        }

        if (_portalMonitor is not null)
        {
            _portalMonitor.AppearanceChanged -= OnChanged;
            _portalMonitor.Dispose();
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_disposed != 0)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, static () =>
        {
            _ = DesktopThemeHost.ApplyLive();
        });
    }
}
