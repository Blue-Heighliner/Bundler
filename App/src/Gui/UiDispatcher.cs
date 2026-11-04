namespace BlueHeighliner.Bundler;

/// <summary>
/// Runs work on the thread that owns the window.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>
    /// Queues work for the window thread.
    /// </summary>
    /// <param name="work">The work.</param>
    void Post(Action work);
}

/// <inheritdoc />
internal sealed class UiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action work)
        => Dispatcher.UIThread.Post(work);
}
