namespace BlueHeighliner.Bundler;

/// <summary>
/// Reports the license of every package in a NuGet feed folder, so that nothing is used whose license nobody has read.
/// </summary>
internal interface INuGetLicenses
{
    /// <summary>
    /// Reads the license of every package in a feed folder. A package is <c>OK</c> when it declares an SPDX expression made only of
    /// permissive licenses, or when its id is on the list of packages whose license a person has read
    /// (<c>licenses/reviewed-nuget.tsv</c>). Anything else (a license file, a bare license URL, an unlisted license) is
    /// <c>REVIEW</c>.
    /// </summary>
    /// <param name="feed">The feed folder.</param>
    /// <returns>One row per package, ordered by id and version.</returns>
    IReadOnlyList<LicenseRow> Report(string feed);

    /// <summary>
    /// Tells whether an SPDX license expression consists only of permissive licenses.
    /// </summary>
    /// <param name="expression">The expression, such as <c>MIT OR Apache-2.0</c>.</param>
    /// <returns><see langword="true" /> when every license in it is permissive.</returns>
    bool IsPermissive(string expression);

    /// <summary>
    /// Writes rows as the tab separated text kept in a bundle.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The text.</returns>
    string Format(IEnumerable<LicenseRow> rows);
}

/// <inheritdoc />
internal sealed partial class NuGetLicenses(IResources resources) : INuGetLicenses
{
    private static readonly HashSet<string> permissive = new(StringComparer.Ordinal)
    {
        "MIT", "MIT-0", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "0BSD", "ISC", "MS-PL", "Zlib", "Unlicense", "CC0-1.0",
        "BSL-1.0", "MPL-2.0", "Python-2.0", "PostgreSQL", "X11", "WTFPL", "BlueOak-1.0.0",
    };

    /// <inheritdoc />
    public IReadOnlyList<LicenseRow> Report(string feed)
    {
        Dictionary<string, string> reviewed = Reviewed();
        List<LicenseRow> rows = [];
        foreach (string path in Directory.EnumerateFiles(feed, "*.nupkg", SearchOption.AllDirectories))
        {
            LicenseRow row = Read(path);
            rows.Add(row.NeedsReview && reviewed.TryGetValue(row.Id.ToLowerInvariant(), out string? license) ? row with { Status = "OK", License = license + " (reviewed)" } : row);
        }

        return rows.OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Version, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public bool IsPermissive(string expression)
    {
        string[] ids = SeparatorPattern().Split(expression).Where(token => token.Length > 0 && token is not ("OR" or "AND" or "WITH")).Select(token => token.TrimEnd('+')).ToArray();
        return ids.Length > 0 && ids.All(id => permissive.Contains(id) || id.EndsWith("exception", StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public string Format(IEnumerable<LicenseRow> rows)
        => string.Concat(rows.Select(row => $"{row.Id}\t{row.Version}\t{row.Status}\t{row.License}\n"));

    [GeneratedRegex(@"[\s()]+")]
    private static partial Regex SeparatorPattern();

    private Dictionary<string, string> Reviewed()
    {
        Dictionary<string, string> reviewed = [];
        foreach (string line in resources.Text("reviewed-nuget.tsv").Split('\n'))
        {
            string[] fields = line.Split('\t');
            if (!line.StartsWith('#') && fields.Length >= 2)
            {
                reviewed[fields[0].ToLowerInvariant()] = fields[1];
            }
        }

        return reviewed;
    }

    private LicenseRow Read(string nupkg)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(nupkg);
            ZipArchiveEntry? spec = zip.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'));
            if (spec is null)
            {
                return new(Path.GetFileName(nupkg), string.Empty, "REVIEW", "package could not be read");
            }

            using Stream stream = spec.Open();
            XElement metadata = XDocument.Load(stream).Root!.Elements().First(element => element.Name.LocalName == "metadata");
            string Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
            XElement? license = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
            string id = Value("id");
            string version = Value("version");
            if (license?.Attribute("type")?.Value == "expression")
            {
                string expression = license.Value.Trim();
                return new(id, version, IsPermissive(expression) ? "OK" : "REVIEW", expression);
            }

            if (license is not null)
            {
                return new(id, version, "REVIEW", "license file " + license.Value.Trim());
            }

            string url = Value("licenseUrl");
            return new(id, version, "REVIEW", "license URL " + (url.Length > 0 ? url : "none declared"));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException)
        {
            return new(Path.GetFileName(nupkg), string.Empty, "REVIEW", "package could not be read");
        }
    }
}
