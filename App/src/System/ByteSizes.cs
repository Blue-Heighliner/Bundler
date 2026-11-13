namespace BlueHeighliner.Bundler;

/// <summary>
/// Reads and writes sizes in bytes the way a person types them.
/// </summary>
internal interface IByteSizes
{
    /// <summary>
    /// Reads a size such as <c>4700000000</c>, <c>4.7G</c>, <c>700M</c>, <c>4GB</c> or <c>4GiB</c>. A plain number is bytes, K, M and G
    /// (with or without a B) are powers of 1000, and KiB, MiB and GiB are powers of 1024.
    /// </summary>
    /// <param name="text">The text; empty or white space means no size.</param>
    /// <returns>The size in bytes, or <see langword="null" /> when the text is empty.</returns>
    /// <exception cref="FormatException">The text is not a size.</exception>
    long? Parse(string? text);

    /// <summary>
    /// Writes a size with the largest decimal unit that keeps it above one, such as <c>4.7 GB</c>.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The text.</returns>
    string Format(long bytes);
}

/// <inheritdoc />
internal sealed partial class ByteSizes : IByteSizes
{
    private readonly Dictionary<string, long> units = new(StringComparer.OrdinalIgnoreCase)
    {
        [string.Empty] = 1,
        ["B"] = 1,
        ["K"] = 1_000,
        ["KB"] = 1_000,
        ["M"] = 1_000_000,
        ["MB"] = 1_000_000,
        ["G"] = 1_000_000_000,
        ["GB"] = 1_000_000_000,
        ["T"] = 1_000_000_000_000,
        ["TB"] = 1_000_000_000_000,
        ["KIB"] = 1_024,
        ["MIB"] = 1_048_576,
        ["GIB"] = 1_073_741_824,
        ["TIB"] = 1_099_511_627_776,
    };

    /// <inheritdoc />
    public long? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = SizePattern().Match(text.Trim());
        if (!match.Success || !units.TryGetValue(match.Groups["unit"].Value, out long unit) || !decimal.TryParse(match.Groups["number"].Value, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out decimal number))
        {
            throw new FormatException($"'{text}' is not a size (examples: 4700000000, 4.7G, 700M, 4GiB)");
        }

        return (long)(number * unit);
    }

    /// <inheritdoc />
    public string Format(long bytes)
        => bytes switch
        {
            >= 1_000_000_000_000 => $"{bytes / 1e12:0.##} TB",
            >= 1_000_000_000 => $"{bytes / 1e9:0.##} GB",
            >= 1_000_000 => $"{bytes / 1e6:0.##} MB",
            >= 1_000 => $"{bytes / 1e3:0.##} KB",
            _ => $"{bytes} B",
        };

    [GeneratedRegex(@"^(?<number>[0-9]+(\.[0-9]+)?)\s*(?<unit>[A-Za-z]*)$")]
    private static partial Regex SizePattern();
}
