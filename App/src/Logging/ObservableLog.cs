namespace BlueHeighliner.Bundler;

/// <summary>
/// Passes log lines on to whoever listens, such as the window.
/// </summary>
internal interface IObservableLog : ILog
{
    /// <summary>Raised for every line written.</summary>
    event Action<LogLine>? Written;
}

/// <inheritdoc />
internal sealed class ObservableLog : IObservableLog
{
    /// <inheritdoc />
    public event Action<LogLine>? Written;

    /// <inheritdoc />
    public void Write(LogLevel level, string text)
        => Written?.Invoke(new() { Level = level, Text = text });
}
