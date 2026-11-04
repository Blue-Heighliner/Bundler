namespace BlueHeighliner.Bundler;

/// <summary>
/// The services each bundle installs, read from the tables in <c>Bundles.md</c> (which is built into the executable), so the
/// window shows exactly what the document says.
/// </summary>
internal interface IBundleServices
{
    /// <summary>
    /// Gets the rows of a bundle's services table.
    /// </summary>
    /// <param name="bundle">The bundle name, the title of its section in <c>Bundles.md</c>.</param>
    /// <returns>The rows in document order, none when the bundle has no section.</returns>
    IReadOnlyList<ServiceRow> For(string bundle);
}

/// <inheritdoc />
internal sealed partial class BundleServices(IResources resources) : IBundleServices
{
    private readonly Dictionary<string, List<ServiceRow>> sections = [];

    private bool loaded;

    /// <inheritdoc />
    public IReadOnlyList<ServiceRow> For(string bundle)
    {
        Load();
        return sections.TryGetValue(bundle, out List<ServiceRow>? rows) ? rows : [];
    }

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    private void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        List<ServiceRow>? current = null;
        bool inTable = false;
        foreach (string line in resources.Text("Bundles.md").Split('\n').Select(text => text.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = [];
                sections[line[3..].Trim()] = current;
                inTable = false;
            }
            else if (current is not null && line.StartsWith('|'))
            {
                string[] cells = Separator().Split(line.Trim().Trim('|')).Select(Clean).ToArray();
                if (cells.Length >= 5 && cells[0] == "Service")
                {
                    inTable = true;
                }
                else if (inTable && cells.Length >= 5 && !cells[0].StartsWith("---", StringComparison.Ordinal))
                {
                    current.Add(new(cells[0], cells[1], cells[2], cells[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), cells[4]));
                }
            }
            else
            {
                inTable = false;
            }
        }
    }

    private string Clean(string cell)
        => Link().Replace(cell.Trim().Replace("\\|", "|"), "$1").Replace("`", string.Empty);
}
