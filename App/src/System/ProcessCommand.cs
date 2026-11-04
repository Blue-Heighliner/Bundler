namespace BlueHeighliner.Bundler;

/// <summary>
/// An external program to run.
/// </summary>
internal sealed record ProcessCommand
{
    /// <summary>Gets the program, a name found on the PATH or a full path.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the arguments, passed exactly as given (no shell is involved).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the folder to run in, or <see langword="null" /> for the current one.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets environment variables to set (a <see langword="null" /> value removes the variable).</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>Gets text to send to the program's standard input.</summary>
    public string? Input { get; init; }

    /// <summary>Gets a value indicating whether each output line is also written to the log.</summary>
    public bool StreamOutput { get; init; }

    /// <summary>Gets a handler that receives every output line (standard output and standard error) as it appears, instead of the log.</summary>
    public Action<string>? OnLine { get; init; }

    /// <summary>Gets a value indicating whether a non-zero exit code throws a <see cref="ProcessFailedException" />.</summary>
    public bool Check { get; init; } = true;

    /// <summary>Gets a value indicating whether the program shares the console of this process (for programs that ask the user something, like sudo).</summary>
    public bool Inherit { get; init; }
}
