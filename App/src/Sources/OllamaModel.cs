namespace BlueHeighliner.Bundler;

/// <summary>
/// What a downloaded Ollama model says about itself.
/// </summary>
internal sealed record OllamaModel
{
    /// <summary>Gets the parameter count, such as <c>25.2B</c>.</summary>
    public required string Parameters { get; init; }

    /// <summary>Gets the quantization, such as <c>Q4_K_M</c>.</summary>
    public required string Quantization { get; init; }

    /// <summary>Gets the lowest Ollama version that can run the model, or an empty string.</summary>
    public required string Requires { get; init; }

    /// <summary>Gets the total size of all the model's files in bytes.</summary>
    public required long Bytes { get; init; }
}
