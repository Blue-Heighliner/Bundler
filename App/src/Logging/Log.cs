namespace BlueHeighliner.Bundler;

/// <summary>
/// Where the work reports what it is doing.
/// </summary>
internal interface ILog
{
    /// <summary>
    /// Writes a line.
    /// </summary>
    /// <param name="level">How important it is.</param>
    /// <param name="text">The text.</param>
    void Write(LogLevel level, string text);
}

/// <summary>
/// Shorthand for writing each kind of line.
/// </summary>
internal static class LogExtensions
{
    extension(ILog log)
    {
        /// <summary>Writes a heading for a stage of the work.</summary>
        /// <param name="text">The text.</param>
        public void Step(string text) => log.Write(LogLevel.Step, text);

        /// <summary>Writes a detail line.</summary>
        /// <param name="text">The text.</param>
        public void Info(string text) => log.Write(LogLevel.Info, text);

        /// <summary>Writes a warning.</summary>
        /// <param name="text">The text.</param>
        public void Warn(string text) => log.Write(LogLevel.Warning, text);

        /// <summary>Writes an error.</summary>
        /// <param name="text">The text.</param>
        public void Error(string text) => log.Write(LogLevel.Error, text);
    }
}
