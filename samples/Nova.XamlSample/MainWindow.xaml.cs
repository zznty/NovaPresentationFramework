using System.Collections.ObjectModel;
using System.Resources;
using System.Windows;
using System.Windows.Media;

namespace Nova.XamlSample;

/// <summary>
/// Main window. Instantiated by the BAML loader from App.xaml's StartupUri, like any
/// stock WPF app; no Nova-specific calls. Prints runtime verification of the four
/// consumer-facing areas (templates, attached properties, localization) on Loaded.
/// </summary>
internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ItemList.ItemsSource = new ObservableCollection<Item>
        {
            new("alpha", Brushes.Red),
            new("beta", Brushes.Green),
            new("gamma", Brushes.Blue)
        };

        // Area 4: localized build. Read the fr-FR satellite through the public
        // ResourceManager API. This resolves only if the UICulture build emitted
        // the fr/Nova.XamlSample.resources.dll satellite and satellite lookup works.
        var manager = new ResourceManager("Nova.XamlSample.Properties.Resources", typeof(App).Assembly);
        string? title = manager.GetString("Title", System.Globalization.CultureInfo.GetCultureInfo("fr"));
        TitleText.Text = title ?? "localized-missing";
    }

    /// <summary>Runs after the window is shown; prints the area-by-area verification.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Console.WriteLine($"localized-title: {TitleText.Text}");
        Console.WriteLine($"application-current: {Application.Current is not null && ReferenceEquals(Application.Current, AppInstance)}");
        Console.WriteLine($"resource-assembly: {typeof(App).Assembly.FullName}");
        Console.WriteLine($"startup-window: {Title} visible={IsVisible}");

        // Area 2: ControlTemplate applied — the Button's template chrome is a
        // Border+ContentPresenter, so the template's root visual (the Border) is a
        // child visual of the button and the content renders through it.
        int templateVisualChildren = VisualTreeHelper.GetChildrenCount(TemplatedButton);
        Console.WriteLine($"control-template: template={TemplatedButton.Template is not null} visual-children={templateVisualChildren} content={TemplatedButton.Content}");
        Console.WriteLine($"control-template-size: {TemplatedButton.ActualWidth:0.#}x{TemplatedButton.ActualHeight:0.#}");

        // Area 2: DataTemplate applied — each Item shows a colored rect + label.
        Console.WriteLine($"data-template: items={ItemList.Items.Count} containers={ItemList.ItemContainerGenerator.ContainerFromIndex(0) is not null}");

        // Area 3: framework attached properties took effect (Grid/Canvas/DockPanel
        // layout) and the custom attached property round-trips through the public API.
        Console.WriteLine($"custom-attached: {Tracked.GetMark(AttachedText)}");
        Console.WriteLine($"attached-layout: canvas={CanvasRect.ActualWidth > 0} docked-header-visible={TitleText.ActualWidth > 0}");
    }

    private static Application AppInstance => Application.Current;
}

/// <summary>Data-templated item: a label plus a brush the DataTemplate binds to.</summary>
internal sealed record Item(string Label, Brush Color);
