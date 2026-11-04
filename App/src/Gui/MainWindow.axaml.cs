namespace BlueHeighliner.Bundler;

/// <summary>
/// The main window: the bundle list, the buttons and the log.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly IMainViewModel model;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow" /> class.
    /// </summary>
    /// <param name="model">What the window shows and does.</param>
    /// <param name="dialogs">Where questions are asked; opens over this window.</param>
    public MainWindow(IMainViewModel model, IDialogs dialogs)
    {
        this.model = model;
        dialogs.Owner = this;
        DataContext = model;
        InitializeComponent();
        Button("SelectAll").Click += (_, _) => model.SelectAll(true);
        Button("SelectNone").Click += (_, _) => model.SelectAll(false);
        Button("Build").Click += async (_, _) => await model.Build();
        Button("Install").Click += async (_, _) => await model.Install();
        Button("Uninstall").Click += async (_, _) => await model.Uninstall();
        Button("Reinstall").Click += async (_, _) => await model.Reinstall();
        Button("Refresh").Click += async (_, _) => await model.Refresh();
        Button("Cancel").Click += (_, _) => model.Cancel();
        Button("ClearLog").Click += (_, _) => model.ClearLog();
        model.Log.CollectionChanged += (_, _) => ScrollLog();
        Opened += async (_, _) => await model.Start();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow" /> class for the XAML previewer.
    /// </summary>
    public MainWindow()
        => throw new NotSupportedException("the window needs its view model");

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (args.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (args.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(args);
        }
    }

    private async void OnLicenseClick(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: LicenseLink link })
        {
            await model.ShowLicense(link);
        }
    }

    private Button Button(string name)
        => this.FindControl<Button>(name)!;

    private void ScrollLog()
    {
        ListBox list = this.FindControl<ListBox>("LogList")!;
        if (model.Log.Count > 0)
        {
            list.ScrollIntoView(model.Log[^1]);
        }
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);
}
