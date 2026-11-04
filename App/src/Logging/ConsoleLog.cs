namespace BlueHeighliner.Bundler;

/// <summary>
/// Prints log lines to the console (standard error, so standard output stays free).
/// </summary>
internal sealed class ConsoleLog(ILogFormat format) : ILog
{
    private readonly object gate = new();

    /// <inheritdoc />
    public void Write(LogLevel level, string text)
    {
        lock (gate)
        {
            Console.Error.WriteLine(format.Format(new() { Level = level, Text = text }));
        }
    }
}
