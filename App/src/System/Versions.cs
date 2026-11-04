namespace BlueHeighliner.Bundler;

/// <summary>
/// Compares version numbers and decides what happens to a component.
/// </summary>
internal interface IVersions
{
    /// <summary>
    /// Compares two version numbers the way <c>sort -V</c> does: runs of digits by value, everything else by text.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Negative when <paramref name="left" /> is lower, zero when equal, positive when higher.</returns>
    int Compare(string left, string right);

    /// <summary>
    /// Compares two Debian package versions (epoch, upstream version and revision) the way <c>dpkg</c> does.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Negative when <paramref name="left" /> is lower, zero when equal, positive when higher.</returns>
    int CompareDebian(string left, string right);

    /// <summary>
    /// Cuts the packaging off a Debian version: the epoch in front, and everything from the first of the given characters on.
    /// </summary>
    /// <param name="debianVersion">The package version, such as <c>1:2.56.0-0ppa1~ubuntu26.04.2</c>.</param>
    /// <param name="stops">The characters the upstream version ends at.</param>
    /// <returns>The upstream version, such as <c>2.56.0</c>.</returns>
    string Upstream(string debianVersion, params char[] stops);

    /// <summary>
    /// Decides what to do with a component.
    /// </summary>
    /// <param name="have">The installed version, or <see langword="null" /> or empty when it is not installed.</param>
    /// <param name="bundle">The version the bundle brings.</param>
    /// <returns>The action.</returns>
    VersionAction Decide(string? have, string bundle);

    /// <summary>
    /// Finds the first version number (<c>x.y</c> or <c>x.y.z</c>) in a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The version number, or an empty string when there is none.</returns>
    string Find(string text);
}

/// <inheritdoc />
internal sealed partial class Versions : IVersions
{
    /// <inheritdoc />
    public int Compare(string left, string right)
    {
        List<string> a = Tokens(left);
        List<string> b = Tokens(right);
        for (int index = 0; index < Math.Min(a.Count, b.Count); index++)
        {
            bool numbers = char.IsDigit(a[index][0]) && char.IsDigit(b[index][0]);
            int result = numbers ? CompareNumbers(a[index], b[index]) : string.CompareOrdinal(a[index], b[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return a.Count.CompareTo(b.Count);
    }

    /// <inheritdoc />
    public int CompareDebian(string left, string right)
    {
        (int epochA, string upstreamA, string revisionA) = SplitDebian(left);
        (int epochB, string upstreamB, string revisionB) = SplitDebian(right);
        if (epochA != epochB)
        {
            return epochA.CompareTo(epochB);
        }

        int upstream = CompareDebianPart(upstreamA, upstreamB);
        return upstream != 0 ? upstream : CompareDebianPart(revisionA, revisionB);
    }

    /// <inheritdoc />
    public string Upstream(string debianVersion, params char[] stops)
    {
        string version = EpochPattern().Replace(debianVersion, string.Empty);
        int stop = version.IndexOfAny(stops);
        return stop < 0 ? version : version[..stop];
    }

    /// <inheritdoc />
    public VersionAction Decide(string? have, string bundle)
    {
        if (string.IsNullOrEmpty(have))
        {
            return VersionAction.Install;
        }

        if (have == bundle)
        {
            return VersionAction.Same;
        }

        return Compare(have, bundle) < 0 ? VersionAction.Upgrade : VersionAction.Newer;
    }

    /// <inheritdoc />
    public string Find(string text)
        => VersionPattern().Match(text) is { Success: true } match ? match.Value : string.Empty;

    [GeneratedRegex(@"^[0-9]+:")]
    private static partial Regex EpochPattern();

    [GeneratedRegex(@"[0-9]+(\.[0-9]+)+")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"[0-9]+|[^0-9]+")]
    private static partial Regex TokenPattern();

    private List<string> Tokens(string version)
        => TokenPattern().Matches(version).Select(match => match.Value).ToList();

    private int CompareNumbers(string left, string right)
    {
        string a = left.TrimStart('0');
        string b = right.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }

    private (int Epoch, string Upstream, string Revision) SplitDebian(string version)
    {
        int epoch = 0;
        int colon = version.IndexOf(':');
        if (colon > 0 && int.TryParse(version[..colon], out int parsed))
        {
            epoch = parsed;
            version = version[(colon + 1)..];
        }

        int dash = version.LastIndexOf('-');
        return dash < 0 ? (epoch, version, string.Empty) : (epoch, version[..dash], version[(dash + 1)..]);
    }

    private int CompareDebianPart(string left, string right)
    {
        int a = 0;
        int b = 0;
        while (a < left.Length || b < right.Length)
        {
            int firstDifference = 0;
            while ((a < left.Length && !char.IsDigit(left[a])) || (b < right.Length && !char.IsDigit(right[b])))
            {
                int orderA = a < left.Length ? DebianOrder(left[a]) : 0;
                int orderB = b < right.Length ? DebianOrder(right[b]) : 0;
                if (orderA != orderB)
                {
                    return orderA.CompareTo(orderB);
                }

                a++;
                b++;
            }

            while (a < left.Length && left[a] == '0')
            {
                a++;
            }

            while (b < right.Length && right[b] == '0')
            {
                b++;
            }

            while (a < left.Length && char.IsDigit(left[a]) && b < right.Length && char.IsDigit(right[b]))
            {
                if (firstDifference == 0)
                {
                    firstDifference = left[a].CompareTo(right[b]);
                }

                a++;
                b++;
            }

            if (a < left.Length && char.IsDigit(left[a]))
            {
                return 1;
            }

            if (b < right.Length && char.IsDigit(right[b]))
            {
                return -1;
            }

            if (firstDifference != 0)
            {
                return firstDifference;
            }
        }

        return 0;
    }

    private int DebianOrder(char character)
    {
        if (character == '~')
        {
            return -1;
        }

        if (char.IsDigit(character))
        {
            return 0;
        }

        return char.IsLetter(character) ? character : character + 256;
    }
}
