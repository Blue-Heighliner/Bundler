namespace BlueHeighliner.Bundler;

/// <summary>
/// What a bundle file says about itself in its <c>bundle.info</c>.
/// </summary>
internal sealed record BundleInfo
{
    /// <summary>Gets the bundle name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets when the bundle was built.</summary>
    public string Built { get; init; } = string.Empty;

    /// <summary>Gets the system the bundle was built on.</summary>
    public string BuiltOn { get; init; } = string.Empty;

    /// <summary>Gets one line for every service in the bundle, with its version.</summary>
    public IReadOnlyList<string> Components { get; init; } = [];

    /// <summary>
    /// Writes the info as the <c>key=value</c> lines of <c>bundle.info</c>.
    /// </summary>
    /// <returns>The file content.</returns>
    public string Format()
    {
        StringBuilder text = new();
        text.Append($"name={Name}\n");
        text.Append($"built={Built}\nbuilt_on={BuiltOn}\n");
        foreach (string component in Components)
        {
            text.Append($"component={component}\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads the content of a <c>bundle.info</c>.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <returns>The info, or <see langword="null" /> when it has no name.</returns>
    public static BundleInfo? Parse(string text)
    {
        Dictionary<string, string> fields = [];
        List<string> components = [];
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals];
            string value = line[(equals + 1)..].TrimEnd('\r');
            if (key == "component")
            {
                components.Add(value);
            }
            else
            {
                fields.TryAdd(key, value);
            }
        }

        if (!fields.TryGetValue("name", out string? name))
        {
            return null;
        }

        return new()
        {
            Name = name,
            Built = fields.GetValueOrDefault("built") ?? string.Empty,
            BuiltOn = fields.GetValueOrDefault("built_on") ?? string.Empty,
            Components = components,
        };
    }
}
