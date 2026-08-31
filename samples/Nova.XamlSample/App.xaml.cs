using System.Windows;
using System.Windows.Threading;

namespace Nova.XamlSample;

/// <summary>
/// Stock WPF application: no custom Main (App.g.cs generates it), no manual message
/// pump, no Nova-specific calls. StartupUri drives MainWindow. Headless CI runs set
/// <c>NOVA_XAMLSAMPLE_AUTO_EXIT=1</c> and the app shuts itself down after a short
/// time through the public <see cref="Application.Shutdown()"/> API, so the generated
/// <c>Main</c> returns and the process exits 0. Interactive runs omit the variable
/// and the window stays open (close the window to exit).
/// </summary>
internal sealed partial class App : Application
{
    /// <summary>
    /// The build is deliberately localized (UICulture=fr-FR in the csproj), so the
    /// WinFX pipeline emits ALL BAML (App.xaml + MainWindow.xaml) into the fr-FR
    /// satellite assembly, not the neutral main assembly. Stock WPF only starts such
    /// an app when the process UI culture is fr-FR (the OS locale normally matches the
    /// UICulture the app was built with). Pin the process UI culture here — BCL-only,
    /// no WPF/Nova APIs — so the sample resolves its satellite and runs to exit 0
    /// under ANY OS locale (no LANG=fr_FR.UTF-8 required). The static ctor runs on
    /// the generated Main's thread before <c>new App()</c> reaches
    /// InitializeComponent/LoadComponent, so the satellite lookup succeeds.
    /// </summary>
    static App()
    {
        var ui = new System.Globalization.CultureInfo("fr-FR");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ui;
        System.Threading.Thread.CurrentThread.CurrentUICulture = ui;
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // CA1812 cannot see the BAML reflection that instantiates MainWindow from
        // StartupUri. A `new MainWindow()` here would steal Application.MainWindow
        // (first window wins — Application.cs) from the real StartupUri window, so
        // instantiate the anchor lazily at idle, after StartupUri has run. If a
        // shutdown timer fires first, skip it (Window construction during
        // shutdown throws).
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (!Dispatcher.HasShutdownStarted)
                {
                    _ = new MainWindow();
                }
            }));

        if (Environment.GetEnvironmentVariable("NOVA_XAMLSAMPLE_AUTO_EXIT") == "1")
        {
            // Drive the dispatcher loop to termination through the public API: a
            // DispatcherTimer (which only ticks because the Linux message loop
            // promotes timers) calls Application.Shutdown() -> Dispatcher
            // shutdown -> DispatcherFrame.Continue goes false -> Run() returns.
            // 5000ms is longer than cold startup (first frame + present), so the
            // CI run actually renders before exiting.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Shutdown(0);
            };
            timer.Start();
        }
    }
}
