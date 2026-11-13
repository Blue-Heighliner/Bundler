namespace BlueHeighliner.Bundler;

/// <summary>
/// Reads and writes bundle files: a tar whose first member is <c>bundle.info</c> and second is <c>SHA256SUMS</c>, followed by the
/// payload.
/// </summary>
internal interface IBundleArchive
{
    /// <summary>
    /// Computes the checksums of everything in a folder except <c>bundle.info</c> and <c>SHA256SUMS</c>. Two folders with
    /// equal checksums hold the same content.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The checksum of each file by its path relative to the folder, with <c>/</c> separators.</returns>
    Task<IReadOnlyDictionary<string, string>> ComputeChecksums(string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Reads the checksums of the content of a bundle file (without <c>bundle.info</c>), from its <c>SHA256SUMS</c>.
    /// </summary>
    /// <param name="file">The bundle, in one file or in parts.</param>
    /// <returns>The checksum of each file by its relative path; empty when the file has none.</returns>
    IReadOnlyDictionary<string, string> ReadChecksums(BundleFile file);

    /// <summary>
    /// Reads the <c>bundle.info</c> of a bundle file, which is its first member and so quick to reach.
    /// </summary>
    /// <param name="file">The bundle, in one file or in parts.</param>
    /// <returns>The info, or <see langword="null" /> when the file is not a readable bundle.</returns>
    BundleInfo? ReadInfo(BundleFile file);

    /// <summary>
    /// Writes <c>bundle.info</c> and <c>SHA256SUMS</c> into a folder and packs the folder into a stream, which is where the
    /// bundle file (or its parts) is made. The stream is left open.
    /// </summary>
    /// <param name="directory">The folder with the payload.</param>
    /// <param name="output">The stream to write the bundle's bytes to.</param>
    /// <param name="info">What the bundle says about itself.</param>
    /// <param name="checksums">The content checksums from <see cref="ComputeChecksums" />.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when everything is written.</returns>
    Task Pack(string directory, Stream output, BundleInfo info, IReadOnlyDictionary<string, string> checksums, CancellationToken cancellation = default);

    /// <summary>
    /// Unpacks a bundle.
    /// </summary>
    /// <param name="file">The bundle, in one file or in parts.</param>
    /// <param name="directory">The folder to unpack into.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when everything is unpacked.</returns>
    /// <exception cref="InvalidDataException">The file is damaged or incomplete.</exception>
    Task Extract(BundleFile file, string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Checks an unpacked bundle against its <c>SHA256SUMS</c>.
    /// </summary>
    /// <param name="directory">The folder the bundle was unpacked into.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The files that are missing or do not match; empty when the bundle is intact.</returns>
    Task<IReadOnlyList<string>> Verify(string directory, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class BundleArchive(IHashing hashing, IBundleFiles files) : IBundleArchive
{
    private static readonly string[] reserved = ["bundle.info", "SHA256SUMS"];

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ComputeChecksums(string directory, CancellationToken cancellation = default)
    {
        SortedDictionary<string, string> sums = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (!reserved.Contains(relative))
            {
                sums[relative] = await hashing.Sha256(path, cancellation);
            }
        }

        return sums;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ReadChecksums(BundleFile file)
    {
        try
        {
            using Stream stream = files.OpenRead(file);
            using TarReader reader = new(stream);
            int seen = 0;
            while (reader.GetNextEntry() is TarEntry entry && seen++ < 4)
            {
                if (Normalize(entry.Name) == "SHA256SUMS" && entry.DataStream is not null)
                {
                    using StreamReader text = new(entry.DataStream);
                    return ParseChecksums(text.ReadToEnd()).Where(pair => pair.Key != "bundle.info").ToDictionary();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
        }

        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public BundleInfo? ReadInfo(BundleFile file)
    {
        try
        {
            using Stream stream = files.OpenRead(file);
            using TarReader reader = new(stream);
            int seen = 0;
            while (reader.GetNextEntry() is TarEntry entry && seen++ < 4)
            {
                if (Normalize(entry.Name) == "bundle.info" && entry.DataStream is not null)
                {
                    using StreamReader text = new(entry.DataStream);
                    return BundleInfo.Parse(text.ReadToEnd());
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
        }

        return null;
    }

    /// <inheritdoc />
    public async Task Pack(string directory, Stream output, BundleInfo info, IReadOnlyDictionary<string, string> checksums, CancellationToken cancellation = default)
    {
        string infoPath = Path.Combine(directory, "bundle.info");
        await File.WriteAllTextAsync(infoPath, info.Format(), cancellation);

        SortedDictionary<string, string> sums = new(checksums.ToDictionary(), StringComparer.Ordinal) { ["bundle.info"] = await hashing.Sha256(infoPath, cancellation) };
        await File.WriteAllTextAsync(Path.Combine(directory, "SHA256SUMS"), string.Concat(sums.Select(pair => $"{pair.Value}  ./{pair.Key}\n")), cancellation);

        await using TarWriter writer = new(output, TarEntryFormat.Pax, leaveOpen: true);
        foreach (string relative in reserved.Concat(checksums.Keys.Order(StringComparer.Ordinal)))
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, relative);
            await using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            PaxTarEntry entry = new(TarEntryType.RegularFile, relative)
            {
                DataStream = source,
                Mode = File.GetUnixFileMode(path),
                ModificationTime = File.GetLastWriteTimeUtc(path),
            };
            await writer.WriteEntryAsync(entry, cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Extract(BundleFile file, string directory, CancellationToken cancellation = default)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(directory);
        try
        {
            await using Stream stream = files.OpenRead(file);
            await using TarReader reader = new(stream);
            while (await reader.GetNextEntryAsync(copyData: false, cancellation) is TarEntry entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    continue;
                }

                string target = Path.GetFullPath(Path.Combine(directory, Normalize(entry.Name)));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"{entry.Name} points outside the folder it is unpacked into");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await entry.ExtractToFileAsync(target, overwrite: true, cancellation);
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"{file.Name} is damaged or incomplete (a part may be missing or cut short)", exception);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> Verify(string directory, CancellationToken cancellation = default)
    {
        string sums = Path.Combine(directory, "SHA256SUMS");
        if (!File.Exists(sums))
        {
            return ["SHA256SUMS"];
        }

        List<string> bad = [];
        foreach ((string relative, string expected) in ParseChecksums(await File.ReadAllTextAsync(sums, cancellation)))
        {
            string path = Path.Combine(directory, relative);
            if (!File.Exists(path) || await hashing.Sha256(path, cancellation) != expected)
            {
                bad.Add(relative);
            }
        }

        return bad;
    }

    private string Normalize(string name)
        => name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;

    private Dictionary<string, string> ParseChecksums(string text)
    {
        Dictionary<string, string> sums = [];
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int gap = line.IndexOf("  ", StringComparison.Ordinal);
            if (gap > 0)
            {
                sums[Normalize(line[(gap + 2)..].TrimEnd('\r'))] = line[..gap];
            }
        }

        return sums;
    }
}
