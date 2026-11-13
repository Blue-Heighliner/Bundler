namespace BlueHeighliner.Bundler;

/// <summary>
/// Finds, reads and writes the built bundles in the Bundles folder. A bundle is one file <c>&lt;name&gt;.bundle</c>, or, when it was
/// built with a size limit that it exceeds, parts <c>&lt;name&gt;.bundle.001</c>, <c>&lt;name&gt;.bundle.002</c> and so on whose bytes
/// joined in order are that one file. Everything above this type treats the two the same.
/// </summary>
internal interface IBundleFiles
{
    /// <summary>
    /// Finds the bundle file or parts of a bundle. When both a single file and parts are there, the one changed last is the bundle.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <returns>The files, or <see langword="null" /> when there are none.</returns>
    BundleFile? Find(string name);

    /// <summary>
    /// Gives the path of the single file of a bundle, whether it is there or not.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <returns>The path.</returns>
    string PathFor(string name);

    /// <summary>
    /// Gives the path of one part of a bundle.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <param name="number">The part number, from 1.</param>
    /// <returns>The path.</returns>
    string PartPath(string name, int number);

    /// <summary>
    /// Opens a bundle for reading as one stream, however many files it is in.
    /// </summary>
    /// <param name="file">The bundle.</param>
    /// <returns>A stream over the joined bytes that can seek.</returns>
    /// <exception cref="InvalidDataException">The parts are not all there or not all complete.</exception>
    Stream OpenRead(BundleFile file);

    /// <summary>
    /// Writes a bundle: what <paramref name="write" /> writes becomes the one file, or, when it is bigger than the limit, parts of
    /// exactly the limit each (the last one holds the rest). The files appear only when everything is written, and then replace
    /// the older bundle of the same name, whichever form it had.
    /// </summary>
    /// <param name="name">The bundle name.</param>
    /// <param name="limit">The most bytes a file may hold, or <see langword="null" /> for no limit (one file).</param>
    /// <param name="write">Writes the bundle's bytes into the stream it is given.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The bundle that was written.</returns>
    Task<BundleFile> Write(string name, long? limit, Func<Stream, Task> write, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class BundleFiles(IPaths paths) : IBundleFiles
{
    /// <inheritdoc />
    public BundleFile? Find(string name)
    {
        BundleFile? whole = File.Exists(PathFor(name)) ? Describe(name, [PathFor(name)], split: false, problem: null) : null;
        BundleFile? parts = FindParts(name);
        return whole is null ? parts : parts is null ? whole : whole.Modified >= parts.Modified ? whole : parts;
    }

    /// <inheritdoc />
    public string PathFor(string name)
        => Path.Combine(paths.Bundles, $"{name}.bundle");

    /// <inheritdoc />
    public string PartPath(string name, int number)
        => Path.Combine(paths.Bundles, $"{name}.bundle.{number:D3}");

    /// <inheritdoc />
    public Stream OpenRead(BundleFile file)
        => file.Problem is not null
            ? throw new InvalidDataException($"{file.Name}: {file.Problem}")
            : file.IsSplit ? new PartedReadStream(file.Paths) : new FileStream(file.Paths[0], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);

    /// <inheritdoc />
    public async Task<BundleFile> Write(string name, long? limit, Func<Stream, Task> write, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(paths.Bundles);
        foreach (string leftover in Directory.EnumerateFiles(paths.Bundles, $"{name}.bundle.*.partial"))
        {
            File.Delete(leftover);
        }

        PartedWriteStream output = new(number => PartPath(name, number) + ".partial", limit);
        try
        {
            await using (output)
            {
                await write(output);
            }
        }
        catch
        {
            foreach (string partial in output.Paths)
            {
                File.Delete(partial);
            }

            throw;
        }

        Remove(name);
        if (output.Paths.Count == 1)
        {
            File.Move(output.Paths[0], PathFor(name), overwrite: true);
        }
        else
        {
            for (int index = 0; index < output.Paths.Count; index++)
            {
                File.Move(output.Paths[index], PartPath(name, index + 1), overwrite: true);
            }
        }

        return Find(name)!;
    }

    private void Remove(string name)
    {
        File.Delete(PathFor(name));
        foreach ((_, string path) in PartFiles(name))
        {
            File.Delete(path);
        }
    }

    private BundleFile? FindParts(string name)
    {
        List<(int Number, string Path)> found = PartFiles(name);
        if (found.Count == 0)
        {
            return null;
        }

        string? problem = null;
        for (int index = 0; index < found.Count && problem is null; index++)
        {
            problem = found[index].Number == index + 1 ? null : $"part {index + 1} is missing";
        }

        long[] sizes = [.. found.Select(part => new FileInfo(part.Path).Length)];
        for (int index = 1; index < sizes.Length - 1 && problem is null; index++)
        {
            problem = sizes[index] == sizes[0] ? null : $"part {index + 1} is not the same size as part 1, so it is incomplete (all parts but the last have the same size)";
        }

        if (problem is null && sizes.Length > 1 && sizes[^1] > sizes[0])
        {
            problem = $"part {sizes.Length} is bigger than part 1, so the parts do not belong together";
        }

        return Describe(name, [.. found.Select(part => part.Path)], split: true, problem);
    }

    private List<(int Number, string Path)> PartFiles(string name)
    {
        if (!Directory.Exists(paths.Bundles))
        {
            return [];
        }

        Regex pattern = new($@"^{Regex.Escape(name)}\.bundle\.(?<number>[0-9]{{3,}})$", RegexOptions.CultureInvariant);
        return [.. Directory.EnumerateFiles(paths.Bundles).Select(path => (Match: pattern.Match(Path.GetFileName(path)), Path: path)).Where(item => item.Match.Success).Select(item => (int.Parse(item.Match.Groups["number"].Value), item.Path)).OrderBy(item => item.Item1)];
    }

    private BundleFile Describe(string name, IReadOnlyList<string> files, bool split, string? problem)
        => new()
        {
            Name = name,
            Paths = files,
            Length = files.Sum(path => new FileInfo(path).Length),
            Modified = files.Max(path => File.GetLastWriteTime(path)),
            IsSplit = split,
            Problem = problem,
        };
}
