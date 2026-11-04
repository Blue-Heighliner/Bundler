namespace BlueHeighliner.Bundler;

/// <summary>
/// How important a log line is.
/// </summary>
internal enum LogLevel
{
    /// <summary>A heading for a stage of the work.</summary>
    Step,

    /// <summary>A detail line.</summary>
    Info,

    /// <summary>Something the user should know about that did not stop the work.</summary>
    Warning,

    /// <summary>The work failed.</summary>
    Error,
}
