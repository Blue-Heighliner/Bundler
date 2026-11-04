namespace BlueHeighliner.Bundler;

/// <summary>
/// Turns log lines into text and back, in the format the command line prints. An elevated child process reports to the
/// window through this text.
/// </summary>
internal interface ILogFormat
{
    /// <summary>
    /// Formats a line the way the command line prints it.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The text, without a line ending.</returns>
    string Format(LogLine line);

    /// <summary>
    /// Reads a line of command line output.
    /// </summary>
    /// <param name="text">The printed text.</param>
    /// <returns>The log line it stands for.</returns>
    LogLine Parse(string text);
}

/// <inheritdoc />
internal sealed class LogFormat : ILogFormat
{
    /// <inheritdoc />
    public string Format(LogLine line)
        => line.Level switch
        {
            LogLevel.Step => $"==> {line.Text}",
            LogLevel.Warning => $"WARNING: {line.Text}",
            LogLevel.Error => $"ERROR: {line.Text}",
            _ => $"    {line.Text}",
        };

    /// <inheritdoc />
    public LogLine Parse(string text)
    {
        if (text.StartsWith("==> ", StringComparison.Ordinal))
        {
            return new() { Level = LogLevel.Step, Text = text[4..] };
        }

        if (text.StartsWith("WARNING: ", StringComparison.Ordinal))
        {
            return new() { Level = LogLevel.Warning, Text = text[9..] };
        }

        if (text.StartsWith("ERROR: ", StringComparison.Ordinal))
        {
            return new() { Level = LogLevel.Error, Text = text[7..] };
        }

        return new() { Level = LogLevel.Info, Text = text.StartsWith("    ", StringComparison.Ordinal) ? text[4..] : text };
    }
}
