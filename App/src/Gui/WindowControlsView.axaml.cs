namespace BlueHeighliner.Bundler;

/// <summary>
/// The minimize, maximize or restore, and close buttons of a window that has no title bar of its own.
/// </summary>
internal sealed partial class WindowControlsView : UserControl
{
    private Window? subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowControlsView" /> class.
    /// </summary>
    public WindowControlsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void Subscribe()
    {
        Unsubscribe();
        if (Owner is { } window)
        {
            subscribed = window;
            window.PropertyChanged += OnWindowPropertyChanged;
            ShowState(window.WindowState);
        }
    }

    private void Unsubscribe()
    {
        if (subscribed is { } window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            subscribed = null;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty)
        {
            ShowState((WindowState)args.NewValue!);
        }
    }

    private void ShowState(WindowState state)
    {
        this.FindControl<Button>("Maximize")!.IsVisible = state != WindowState.Maximized;
        this.FindControl<Button>("Restore")!.IsVisible = state == WindowState.Maximized;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs args)
    {
        if (Owner is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs args)
    {
        if (Owner is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs args)
        => Owner?.Close();
}
