namespace BlueHeighliner.Bundler;

/// <summary>
/// One line of an uninstall script: a named action with its text arguments and, for the conditions, the steps it guards.
/// </summary>
internal sealed record UninstallStep
{
    /// <summary>Gets the action, such as <c>rm</c> or <c>apt-remove-added</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the arguments of the action.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Gets the steps that run only when the condition of this step holds; empty for every action that is not a condition.</summary>
    public IReadOnlyList<UninstallStep> Then { get; init; } = [];
}
