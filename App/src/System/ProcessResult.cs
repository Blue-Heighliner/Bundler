namespace BlueHeighliner.Bundler;

/// <summary>
/// What a finished program produced.
/// </summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">Everything it wrote to standard output.</param>
/// <param name="Error">Everything it wrote to standard error.</param>
internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    /// <summary>Gets a value indicating whether the exit code was zero.</summary>
    public bool Success => ExitCode == 0;

    /// <summary>Gets the output with its surrounding white space removed.</summary>
    public string Text => Output.Trim();
}
