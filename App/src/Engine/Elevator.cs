namespace BlueHeighliner.Bundler;

/// <summary>
/// Starts this app again as root. The app always runs as root so that it has one app data folder, root's, whoever started it.
/// </summary>
internal interface IElevator
{
    /// <summary>
    /// Runs this app with the given command line as root: through <c>sudo</c> in the same terminal, or through <c>pkexec</c> with a
    /// graphical password prompt when there is no terminal to ask in. The folder with the bundle files is passed on as
    /// <c>--bundles</c> (as a full path, so it means the same to the new process) and so are the variables a window needs to
    /// reach the user's display.
    /// </summary>
    /// <param name="arguments">The command line the app was started with, without <c>--bundles</c>.</param>
    /// <param name="useTerminal">Whether the program shares this terminal or its output is read line by line into the log.</param>
    /// <param name="cancellation">Stops the program when cancelled.</param>
    /// <returns>The exit code of the program.</returns>
    /// <exception cref="InvalidOperationException">Neither <c>sudo</c> nor <c>pkexec</c> is available.</exception>
    Task<int> Run(IReadOnlyList<string> arguments, bool useTerminal, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Elevator(IMachine machine, IPaths paths, IProcessRunner runner, ILogFormat format, ILog log) : IElevator
{
    /// <inheritdoc />
    public async Task<int> Run(IReadOnlyList<string> arguments, bool useTerminal, CancellationToken cancellation = default)
    {
        string[] prefix = useTerminal || machine.FindExecutable("pkexec") is null ? ["sudo"] : ["pkexec"];
        if (machine.FindExecutable(prefix[0]) is null)
        {
            throw new InvalidOperationException($"the app needs administrator rights and {prefix[0]} is not available");
        }

        ProcessResult result = await runner.Run(
            new()
            {
                FileName = prefix[0],
                Arguments = [.. prefix.Skip(1), "env", .. machine.DisplayEnvironment.Select(pair => $"{pair.Key}={pair.Value}"), .. machine.SelfCommand, .. arguments, "--bundles", paths.Bundles],
                Inherit = useTerminal,
                Check = false,
                OnLine = useTerminal ? null : line => log.Write(format.Parse(line).Level, format.Parse(line).Text),
            },
            cancellation);
        return result.ExitCode;
    }
}
