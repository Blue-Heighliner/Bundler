namespace BlueHeighliner.Bundler;

/// <summary>
/// One line of output.
/// </summary>
internal sealed record LogLine
{
    /// <summary>Gets how important the line is.</summary>
    public required LogLevel Level { get; init; }

    /// <summary>Gets the text.</summary>
    public required string Text { get; init; }
}
