namespace BlueHeighliner.Bundler;

/// <summary>
/// The command line of the app: list, build, install and uninstall without the window.
/// </summary>
internal interface ICommandLine
{
    /// <summary>
    /// Runs a command.
    /// </summary>
    /// <param name="arguments">The command line arguments.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The exit code: zero when the command worked.</returns>
    Task<int> Run(IReadOnlyList<string> arguments, CancellationToken cancellation = default);

    /// <summary>Gets the help text.</summary>
    string Help { get; }
}

/// <inheritdoc />
internal sealed class CommandLine(IBundleEngine engine, ILog log) : ICommandLine
{
    /// <inheritdoc />
    public string Help
        => """
            Bundler builds bundles on an online computer and installs them on an offline Ubuntu 26.04 computer.

              Bundler                       open the window
              Bundler list                  list the bundles
              Bundler build <bundle|all>    online computer: download the latest stable versions into <bundle>.bundle in the Bundles folder
              Bundler install <bundle>      offline computer: install the bundle file found in the Bundles folder
              Bundler uninstall <bundle>    offline computer: remove what the bundle installed
              Bundler reinstall <bundle>    offline computer: uninstall the bundle (if installed) and install it again from its file

            Options: -y, --yes        do not ask for confirmation
                     --bundles PATH   use this folder for the bundle files instead of "Bundles" next to the app

            The app always runs as root (it starts itself again through sudo when needed), so what it keeps is in root's app data folder.
            """;

    /// <inheritdoc />
    public async Task<int> Run(IReadOnlyList<string> arguments, CancellationToken cancellation = default)
    {
        string command = arguments.Count > 0 ? arguments[0] : "help";
        bool yes = false;
        List<string> names = [];
        for (int index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "-y" or "--yes":
                    yes = true;
                    break;
                case { } option when option.StartsWith('-'):
                    log.Error($"unknown option {option}");
                    return 2;
                default:
                    names.Add(arguments[index]);
                    break;
            }
        }

        try
        {
            switch (command)
            {
                case "list" or "ls":
                    Print(engine.List());
                    return 0;
                case "build":
                    return await Required(names, command, async () => await engine.Build(names, cancellation));
                case "install" or "uninstall" or "remove" or "reinstall":
                    return await Required(names, command, () => Change(command, names, yes, cancellation));
                case "help" or "-h" or "--help":
                    Console.Error.WriteLine(Help);
                    return 0;
                default:
                    log.Error($"unknown command '{command}'");
                    Console.Error.WriteLine(Help);
                    return 2;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            log.Warn("cancelled");
            return 130;
        }
        catch (Exception exception)
        {
            log.Error(exception.Message);
            return 1;
        }
    }

    private async Task<int> Required(List<string> names, string command, Func<Task> work)
    {
        if (names.Count == 0)
        {
            log.Error($"usage: {command} <bundle>");
            return 2;
        }

        await work();
        return 0;
    }

    private Task Change(string command, List<string> names, bool yes, CancellationToken cancellation)
    {
        RunOptions options = new() { Confirm = yes ? null : Ask };
        return command switch
        {
            "install" => engine.Install(names, options, cancellation),
            "reinstall" => engine.Reinstall(names, options, cancellation),
            _ => engine.Uninstall(names, options, cancellation),
        };
    }

    private Task<bool> Ask(string question)
    {
        Console.Error.Write($"{question} [y/N] ");
        string? answer = Console.In.ReadLine();
        return Task.FromResult(answer is { Length: > 0 } && char.ToLowerInvariant(answer[0]) == 'y');
    }

    private void Print(IReadOnlyList<BundleStatus> rows)
    {
        string[] modified = [.. rows.Select(row => row.FileModified is DateTime time ? time.ToString("yyyy-MM-dd HH:mm") : "-")];
        int name = Math.Max(4, rows.Max(row => row.Name.Length));
        int target = Math.Max(6, rows.Max(row => row.Target.Length));
        Console.Out.WriteLine($"\n{"NAME".PadRight(name)} {"STATE",-10} {"TARGET".PadRight(target)} {"BUNDLE FILE",-16} {"NEEDS",-6} DESCRIPTION");
        for (int index = 0; index < rows.Count; index++)
        {
            BundleStatus row = rows[index];
            string state = row.State switch
            {
                BundleStanding.NotBuilt => "not built",
                BundleStanding.Available => "available",
                _ => "installed",
            };
            Console.Out.WriteLine($"{row.Name.PadRight(name)} {state,-10} {row.Target.PadRight(target)} {modified[index],-16} {(row.Requires.Count > 0 ? string.Join(',', row.Requires) : "-"),-6} {row.Description}");
        }

        Console.Out.WriteLine("\ninstalled: on this computer   available: built, ready to install   not built: run \"build <name>\" on an online computer\n");
    }
}
