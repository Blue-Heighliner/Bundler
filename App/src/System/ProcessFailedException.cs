namespace BlueHeighliner.Bundler;

/// <summary>
/// A program that was expected to succeed did not.
/// </summary>
internal sealed class ProcessFailedException(string message) : Exception(message);
