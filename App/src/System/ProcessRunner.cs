namespace BlueHeighliner.Bundler;

/// <summary>
/// Runs external programs.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs a program and waits for it to finish.
    /// </summary>
    /// <param name="command">What to run.</param>
    /// <param name="cancellation">Stops the program when cancelled.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ProcessFailedException">The program could not be started, or failed while <see cref="ProcessCommand.Check" /> is set.</exception>
    Task<ProcessResult> Run(ProcessCommand command, CancellationToken cancellation = default);
}

/// <summary>
/// Shorthand for the common ways of running a program.
/// </summary>
internal static class ProcessRunnerExtensions
{
    extension(IProcessRunner runner)
    {
        /// <summary>Runs a program that has to succeed and returns its trimmed output.</summary>
        /// <param name="fileName">The program.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The trimmed standard output.</returns>
        public async Task<string> Capture(string fileName, IReadOnlyList<string> arguments)
            => (await runner.Run(new() { FileName = fileName, Arguments = arguments })).Text;

        /// <summary>Runs a program that has to succeed, writing its output to the log.</summary>
        /// <param name="fileName">The program.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task that completes when the program has finished.</returns>
        public Task Execute(string fileName, IReadOnlyList<string> arguments)
            => runner.Run(new() { FileName = fileName, Arguments = arguments, StreamOutput = true });

        /// <summary>Runs a program and reports whether it succeeded, without throwing when it does not.</summary>
        /// <param name="fileName">The program.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        public Task<ProcessResult> Try(string fileName, IReadOnlyList<string> arguments)
            => runner.Run(new() { FileName = fileName, Arguments = arguments, Check = false });
    }
}

/// <inheritdoc />
internal sealed class ProcessRunner(ILog log) : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessResult> Run(ProcessCommand command, CancellationToken cancellation = default)
    {
        ProcessStartInfo info = new(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory ?? string.Empty,
            RedirectStandardInput = command.Input is not null,
            RedirectStandardOutput = !command.Inherit,
            RedirectStandardError = !command.Inherit,
        };

        foreach (string argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach ((string name, string? value) in command.Environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        using Process process = new() { StartInfo = info };
        StringBuilder output = new();
        StringBuilder error = new();

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!command.Check)
            {
                return new(127, string.Empty, exception.Message);
            }

            throw new ProcessFailedException($"could not start {command.FileName}: {exception.Message}");
        }

        using CancellationTokenRegistration registration = cancellation.Register(() => Kill(process));
        Task[] readers = command.Inherit ? [] : [Pump(process.StandardOutput, output, command), Pump(process.StandardError, error, command)];

        if (command.Input is not null)
        {
            await process.StandardInput.WriteAsync(command.Input);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(readers);
        cancellation.ThrowIfCancellationRequested();

        ProcessResult result = new(process.ExitCode, output.ToString(), error.ToString());
        if (command.Check && !result.Success)
        {
            string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
            throw new ProcessFailedException($"{command.FileName} {string.Join(' ', command.Arguments)} failed with exit code {result.ExitCode}: {detail}");
        }

        return result;
    }

    private void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task Pump(StreamReader reader, StringBuilder target, ProcessCommand command)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            target.AppendLine(line);
            if (command.OnLine is not null)
            {
                command.OnLine(line);
            }
            else if (command.StreamOutput && line.Length > 0)
            {
                log.Info(line);
            }
        }
    }
}
