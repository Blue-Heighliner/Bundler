namespace BlueHeighliner.Bundler;

/// <summary>
/// Describes a folder of <c>.deb</c> files as a flat apt repository (<c>Packages</c> and <c>Release</c>), and reads that
/// description back.
/// </summary>
internal interface IDebIndex
{
    /// <summary>
    /// Reads the control fields of a <c>.deb</c> file.
    /// </summary>
    /// <param name="deb">The file.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The control file text.</returns>
    Task<string> Control(string deb, CancellationToken cancellation = default);

    /// <summary>
    /// Writes <c>Packages</c> and <c>Release</c> for the <c>.deb</c> files in a folder. The files do not depend on the time,
    /// so the same packages always give the same index.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The packages in the folder.</returns>
    Task<IReadOnlyList<DebPackage>> Write(string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Reads the packages listed in the <c>Packages</c> file of a folder.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The packages, none when there is no index.</returns>
    IReadOnlyList<DebPackage> Read(string directory);

    /// <summary>
    /// Reads one field from control file text.
    /// </summary>
    /// <param name="control">The text.</param>
    /// <param name="field">The field name.</param>
    /// <returns>The value, or an empty string.</returns>
    string Field(string control, string field);
}

/// <inheritdoc />
internal sealed class DebIndex(IProcessRunner runner, IHashing hashing) : IDebIndex
{
    /// <inheritdoc />
    public async Task<string> Control(string deb, CancellationToken cancellation = default)
        => (await runner.Run(new() { FileName = "dpkg-deb", Arguments = ["-f", deb] }, cancellation)).Output;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DebPackage>> Write(string directory, CancellationToken cancellation = default)
    {
        StringBuilder index = new();
        List<DebPackage> packages = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.deb").Order(StringComparer.Ordinal))
        {
            string control = (await Control(path, cancellation)).TrimEnd('\n');
            string fileName = Path.GetFileName(path);
            index.Append(control).Append($"\nFilename: ./{fileName}\nSize: {new FileInfo(path).Length}\nSHA256: {await hashing.Sha256(path, cancellation)}\n\n");
            packages.Add(new(Field(control, "Package"), Field(control, "Version"), fileName));
        }

        string packagesPath = Path.Combine(directory, "Packages");
        await File.WriteAllTextAsync(packagesPath, index.ToString(), cancellation);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Release"),
            $"Origin: bundler\nLabel: bundler\nSuite: local\nCodename: local\nArchitectures: amd64\nSHA256:\n {await hashing.Sha256(packagesPath, cancellation)} {new FileInfo(packagesPath).Length} Packages\n",
            cancellation);
        return packages;
    }

    /// <inheritdoc />
    public IReadOnlyList<DebPackage> Read(string directory)
    {
        string path = Path.Combine(directory, "Packages");
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllText(path).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => new DebPackage(Field(paragraph, "Package"), Field(paragraph, "Version"), Field(paragraph, "Filename").TrimStart('.', '/')))
            .Where(package => package.Package.Length > 0)
            .ToList();
    }

    /// <inheritdoc />
    public string Field(string control, string field)
    {
        foreach (string line in control.Split('\n'))
        {
            if (line.StartsWith(field + ": ", StringComparison.Ordinal))
            {
                return line[(field.Length + 2)..].Trim();
            }
        }

        return string.Empty;
    }
}
