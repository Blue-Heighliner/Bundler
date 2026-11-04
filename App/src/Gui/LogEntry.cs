namespace BlueHeighliner.Bundler;

/// <summary>
/// One line of the log panel, ready to show.
/// </summary>
internal sealed record LogEntry
{
    private static readonly IBrush step = new SolidColorBrush(Color.Parse("#569CD6"));

    private static readonly IBrush warning = new SolidColorBrush(Color.Parse("#E2C08D"));

    private static readonly IBrush error = new SolidColorBrush(Color.Parse("#F48771"));

    private static readonly IBrush info = new SolidColorBrush(Color.Parse("#CCCCCC"));

    /// <summary>Gets the text.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the level of the line.</summary>
    public required LogLevel Level { get; init; }

    /// <summary>Gets the color the line is drawn in.</summary>
    public IBrush Foreground => Level switch
    {
        LogLevel.Step => step,
        LogLevel.Warning => warning,
        LogLevel.Error => error,
        _ => info,
    };

    /// <summary>Gets the weight the line is drawn in.</summary>
    public FontWeight Weight => Level == LogLevel.Step ? FontWeight.Bold : FontWeight.Normal;
}
