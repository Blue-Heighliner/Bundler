namespace BlueHeighliner.Bundler;

/// <summary>
/// What the main window shows and does.
/// </summary>
internal interface IMainViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the bundle rows.</summary>
    ObservableCollection<BundleRowViewModel> Bundles { get; }

    /// <summary>Gets the lines of the log.</summary>
    ObservableCollection<LogEntry> Log { get; }

    /// <summary>Gets or sets the bundle whose details are shown.</summary>
    BundleRowViewModel? Current { get; set; }

    /// <summary>Gets a value indicating whether a bundle's details are shown.</summary>
    bool HasCurrent { get; }

    /// <summary>Gets what the app is doing, for the status bar.</summary>
    string Activity { get; }

    /// <summary>Gets a notice shown above the list when the app cannot do its work here, or empty.</summary>
    string Notice { get; }

    /// <summary>
    /// Gets or sets the size limit for the files of a build, as typed (for example <c>4.7G</c>, <c>700M</c> or <c>4700000000</c>); empty
    /// means no limit. A bundle bigger than the limit is built in parts of exactly that size.
    /// </summary>
    string MaxSize { get; set; }

    /// <summary>Gets a value indicating whether work is running.</summary>
    bool IsBusy { get; }

    /// <summary>Gets a value indicating whether builds can start now.</summary>
    bool CanBuild { get; }

    /// <summary>Gets a value indicating whether installs and uninstalls can start now.</summary>
    bool CanInstall { get; }

    /// <summary>Gets a value indicating whether the refresh button can be used now.</summary>
    bool CanRefresh { get; }

    /// <summary>
    /// Starts listening to the log and loads the list.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    Task Start();

    /// <summary>
    /// Reloads the list.
    /// </summary>
    /// <returns>A task that completes when the list is loaded.</returns>
    Task Refresh();

    /// <summary>
    /// Empties the log.
    /// </summary>
    void ClearLog();

    /// <summary>
    /// Opens the full text of a license, together with the other licenses of its service.
    /// </summary>
    /// <param name="link">The license that was clicked.</param>
    /// <returns>A task that completes when the license window is closed.</returns>
    Task ShowLicense(LicenseLink link);

    /// <summary>
    /// Ticks or unticks every row.
    /// </summary>
    /// <param name="selected">The new state of the ticks.</param>
    void SelectAll(bool selected);

    /// <summary>
    /// Builds the ticked bundles into the Bundles folder. Any number of bundles can be ticked, but only one action (build, install
    /// uninstall or reinstall) runs at a time: this does nothing while another one runs.
    /// </summary>
    /// <returns>A task that completes when the work ends.</returns>
    Task Build();

    /// <summary>
    /// Installs the ticked bundles after asking.
    /// </summary>
    /// <returns>A task that completes when the work ends.</returns>
    Task Install();

    /// <summary>
    /// Uninstalls the ticked bundles after asking.
    /// </summary>
    /// <returns>A task that completes when the work ends.</returns>
    Task Uninstall();

    /// <summary>
    /// Uninstalls the ticked bundles that are installed and installs all the ticked ones again, after asking once.
    /// </summary>
    /// <returns>A task that completes when the work ends.</returns>
    Task Reinstall();

    /// <summary>
    /// Stops the work that is running.
    /// </summary>
    void Cancel();
}

/// <inheritdoc cref="IMainViewModel" />
internal sealed class MainViewModel(IBundleEngine engine, IMachine machine, IBundleServices services, IByteSizes sizes, IObservableLog log, IDialogs dialogs, IUiDispatcher dispatcher) : ViewModelBase, IMainViewModel
{
    private readonly int maxLines = 5000;
    private CancellationTokenSource? cancellation;
    private BundleRowViewModel? current;
    private string activity = "Ready";
    private string maxSize = string.Empty;
    private bool busy;

    /// <inheritdoc />
    public ObservableCollection<BundleRowViewModel> Bundles { get; } = [];

    /// <inheritdoc />
    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <inheritdoc />
    public BundleRowViewModel? Current
    {
        get => current;
        set
        {
            if (Set(ref current, value))
            {
                Raise(nameof(HasCurrent));
            }
        }
    }

    /// <inheritdoc />
    public bool HasCurrent => current is not null;

    /// <inheritdoc />
    public string Activity
    {
        get => activity;
        private set => Set(ref activity, value);
    }

    /// <inheritdoc />
    public string Notice => machine.IsLinux ? string.Empty : "Bundler builds and installs bundles on Ubuntu Linux only. This window can only show the list here.";

    /// <inheritdoc />
    public string MaxSize
    {
        get => maxSize;
        set => Set(ref maxSize, value ?? string.Empty);
    }

    /// <inheritdoc />
    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (Set(ref busy, value))
            {
                RaiseButtons();
            }
        }
    }

    /// <inheritdoc />
    public bool CanBuild => !busy && machine.IsLinux && Selected().Count > 0;

    /// <inheritdoc />
    public bool CanInstall => CanBuild;

    /// <inheritdoc />
    public bool CanRefresh => !busy;

    /// <inheritdoc />
    public Task Start()
    {
        log.Written += OnWritten;
        return Refresh();
    }

    /// <inheritdoc />
    public async Task Refresh()
    {
        HashSet<string> ticked = Bundles.Where(row => row.IsSelected).Select(row => row.Name).ToHashSet();
        string? shown = current?.Name;
        IReadOnlyList<BundleStatus> statuses;
        try
        {
            statuses = await Task.Run(engine.List);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Error($"the bundle list could not be read: {exception.Message}");
            return;
        }

        foreach (BundleRowViewModel row in Bundles)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Bundles.Clear();
        foreach (BundleStatus status in statuses)
        {
            BundleRowViewModel row = new(status, services.For(status.Name), ticked.Contains(status.Name));
            row.PropertyChanged += OnRowChanged;
            Bundles.Add(row);
        }

        Current = Bundles.FirstOrDefault(row => row.Name == shown) ?? Bundles.FirstOrDefault();
        RaiseButtons();
    }

    /// <inheritdoc />
    public void ClearLog()
        => Log.Clear();

    /// <inheritdoc />
    public Task ShowLicense(LicenseLink link)
        => dialogs.ShowLicenses(link.Service, link.All, link.Id);

    /// <inheritdoc />
    public void SelectAll(bool selected)
    {
        foreach (BundleRowViewModel row in Bundles)
        {
            row.IsSelected = selected;
        }
    }

    /// <inheritdoc />
    public Task Build()
    {
        if (!CanBuild)
        {
            return Task.CompletedTask;
        }

        long? limit;
        try
        {
            limit = sizes.Parse(MaxSize);
        }
        catch (FormatException exception)
        {
            log.Error(exception.Message);
            return Task.CompletedTask;
        }

        List<string> names = Selected();
        return Run("Build", "Building", null, token => engine.Build(names, limit, token));
    }

    /// <inheritdoc />
    public Task Install()
    {
        if (!CanInstall)
        {
            return Task.CompletedTask;
        }

        List<string> names = Selected();
        return Run("Install", "Installing", () => dialogs.Confirm($"Install {string.Join(", ", engine.InstallPlan(names))} on this computer?"), token => Change("install", names, token));
    }

    /// <inheritdoc />
    public Task Uninstall()
    {
        if (!CanInstall)
        {
            return Task.CompletedTask;
        }

        List<string> names = Selected();
        return Run("Uninstall", "Uninstalling", () => dialogs.Confirm($"Uninstall {string.Join(", ", names)} from this computer?"), token => Change("uninstall", names, token));
    }

    /// <inheritdoc />
    public Task Reinstall()
    {
        if (!CanInstall)
        {
            return Task.CompletedTask;
        }

        List<string> names = Selected();
        return Run("Reinstall", "Reinstalling", () => dialogs.Confirm($"Reinstall {string.Join(", ", names)}? What is installed of them is removed first."), token => Change("reinstall", names, token));
    }

    /// <inheritdoc />
    public void Cancel()
        => cancellation?.Cancel();

    private List<string> Selected()
        => Bundles.Where(row => row.IsSelected).Select(row => row.Name).ToList();

    private async Task Run(string title, string activityText, Func<Task<bool>>? confirm, Func<CancellationToken, Task> work)
    {
        using CancellationTokenSource source = new();
        cancellation = source;
        IsBusy = true;
        Activity = activityText;
        try
        {
            if (confirm is not null && !await confirm())
            {
                return;
            }

            await Task.Run(() => work(source.Token));
            log.Step($"{title} finished");
        }
        catch (OperationCanceledException)
        {
            log.Warn($"{title} cancelled");
        }
        catch (Exception exception)
        {
            log.Error(exception.Message);
        }
        finally
        {
            cancellation = null;
            IsBusy = false;
            Activity = "Ready";
            await Refresh();
        }
    }

    private Task Change(string command, List<string> names, CancellationToken token)
    {
        RunOptions options = new();
        return command switch
        {
            "install" => engine.Install(names, options, token),
            "reinstall" => engine.Reinstall(names, options, token),
            _ => engine.Uninstall(names, options, token),
        };
    }

    private void OnWritten(LogLine line)
        => dispatcher.Post(() =>
        {
            Log.Add(new() { Text = line.Text, Level = line.Level });
            if (Log.Count > maxLines)
            {
                Log.RemoveAt(0);
            }
        });

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BundleRowViewModel.IsSelected))
        {
            RaiseButtons();
        }
    }

    private void RaiseButtons()
    {
        Raise(nameof(CanBuild));
        Raise(nameof(CanInstall));
        Raise(nameof(CanRefresh));
    }
}
