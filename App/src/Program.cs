namespace BlueHeighliner.Bundler;

/// <summary>
/// The application entry point: the command line when there are arguments, the window otherwise. On Linux the app always runs as
/// root, so when it is started as anyone else it starts itself again through sudo or pkexec and returns that run's exit code.
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// Starts the app. Nothing Avalonia-dependent may run before the app builder is configured.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        (string? bundles, string[] rest) = SplitBundles(args);
        using CancellationTokenSource cancel = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancel.Cancel();
        };

        using ServiceProvider services = new ServiceCollection().AddBundler(provider => new ConsoleLog(provider.GetRequiredService<ILogFormat>()), bundles).BuildServiceProvider();
        IMachine machine = services.GetRequiredService<IMachine>();
        if (machine.IsLinux && !machine.IsPrivileged)
        {
            return Restart(services, rest, cancel.Token);
        }

        if (rest.Length == 0)
        {
            try
            {
                return BuildAvaloniaApp(services.GetRequiredService<IPaths>().Bundles).StartWithClassicDesktopLifetime(rest);
            }
            catch (Exception exception) when (exception.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
            {
                services.GetRequiredService<ILog>().Error("the window could not connect to the display. The window runs as root, which an X server only allows when it was given the user's X authority file (XAUTHORITY, which GNOME sets up) or was told to (run `xhost +si:localuser:root` as the logged-in user). The command line works without a display.");
                return 1;
            }
        }

        return services.GetRequiredService<ICommandLine>().Run(rest, cancel.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds the Avalonia configuration. Also used by the headless tests.
    /// </summary>
    /// <param name="bundles">The folder with the bundle files, or <see langword="null" /> for <c>bundles</c> next to the executable.</param>
    /// <returns>The configured app builder.</returns>
    public static AppBuilder BuildAvaloniaApp(string? bundles = null)
        => AppBuilder.Configure(() => new App { BundlesDirectory = bundles })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static (string? Bundles, string[] Remaining) SplitBundles(string[] args)
    {
        int index = Array.IndexOf(args, "--bundles");
        return index >= 0 && index + 1 < args.Length ? (args[index + 1], [.. args.Take(index), .. args.Skip(index + 2)]) : (null, args);
    }

    private static int Restart(ServiceProvider services, string[] args, CancellationToken cancellation)
    {
        try
        {
            return services.GetRequiredService<IElevator>().Run(args, !Console.IsInputRedirected, cancellation).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            services.GetRequiredService<ILog>().Error(exception.Message);
            return 1;
        }
    }
}
