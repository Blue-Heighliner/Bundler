namespace BlueHeighliner.Bundler;

/// <summary>
/// The Avalonia application: wires up the services and opens the main window.
/// </summary>
internal sealed partial class App : Application
{
    private ServiceProvider? services;

    /// <summary>Gets the folder with the bundle files, or <see langword="null" /> for <c>Bundles</c> next to the executable.</summary>
    public string? BundlesDirectory { get; init; }

    /// <inheritdoc />
    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            services = new ServiceCollection()
                .AddSingleton<IObservableLog, ObservableLog>()
                .AddBundler(provider => provider.GetRequiredService<IObservableLog>(), BundlesDirectory)
                .AddSingleton<IUiDispatcher, UiDispatcher>()
                .AddSingleton<IDialogs, Dialogs>()
                .AddSingleton<IBundleServices, BundleServices>()
                .AddSingleton<IMainViewModel, MainViewModel>()
                .BuildServiceProvider();
            desktop.MainWindow = new MainWindow(services.GetRequiredService<IMainViewModel>(), services.GetRequiredService<IDialogs>());
            desktop.Exit += (_, _) => services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
