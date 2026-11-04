namespace BlueHeighliner.Bundler;

/// <summary>
/// How an install, uninstall or reinstall is run.
/// </summary>
internal sealed record RunOptions
{
    /// <summary>Gets a question to ask before the computer is changed; <see langword="null" /> goes ahead without asking.</summary>
    public Func<string, Task<bool>>? Confirm { get; init; }
}
