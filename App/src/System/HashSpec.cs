namespace BlueHeighliner.Bundler;

/// <summary>
/// A checksum a download is expected to have, written <c>sha256:HEX</c> or <c>sha512:HEX</c>.
/// </summary>
/// <param name="Algorithm">The hash algorithm, <c>sha256</c> or <c>sha512</c>.</param>
/// <param name="Hex">The expected hash in lower case hexadecimal.</param>
internal sealed record HashSpec(string Algorithm, string Hex)
{
    /// <summary>
    /// Reads a checksum written <c>algorithm:hex</c>.
    /// </summary>
    /// <param name="text">The text, or <see langword="null" /> or empty when there is none.</param>
    /// <returns>The checksum, or <see langword="null" /> when no text was given.</returns>
    /// <exception cref="FormatException">The text is not <c>sha256:HEX</c> or <c>sha512:HEX</c>.</exception>
    public static HashSpec? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(':', 2);
        if (parts.Length != 2 || parts[0] is not ("sha256" or "sha512") || parts[1].Length == 0)
        {
            throw new FormatException($"unknown hash '{text}'");
        }

        return new(parts[0], parts[1].ToLowerInvariant());
    }
}
