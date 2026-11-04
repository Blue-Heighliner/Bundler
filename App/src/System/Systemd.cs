namespace BlueHeighliner.Bundler;

/// <summary>
/// Controls systemd services. Where systemd is not the running init system (a chroot, a container) nothing is done and the
/// services start at the next boot.
/// </summary>
internal interface ISystemd
{
    /// <summary>Gets a value indicating whether systemd is running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Reloads the unit files.
    /// </summary>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when they are reloaded.</returns>
    Task Reload(CancellationToken cancellation = default);

    /// <summary>
    /// Runs a <c>systemctl</c> command for a service; failures are ignored.
    /// </summary>
    /// <param name="verb">The command, such as <c>enable</c>, <c>start</c>, <c>restart</c> or <c>disable --now</c>.</param>
    /// <param name="service">The service.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the command has run.</returns>
    Task Control(string verb, string service, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Systemd(IProcessRunner runner) : ISystemd
{
    /// <inheritdoc />
    public bool IsRunning => Directory.Exists("/run/systemd/system");

    /// <inheritdoc />
    public async Task Reload(CancellationToken cancellation = default)
    {
        if (IsRunning)
        {
            await runner.Run(new() { FileName = "systemctl", Arguments = ["daemon-reload"], Check = false }, cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Control(string verb, string service, CancellationToken cancellation = default)
    {
        if (IsRunning)
        {
            await runner.Run(new() { FileName = "systemctl", Arguments = [.. verb.Split(' '), service], Check = false }, cancellation);
        }
    }
}
