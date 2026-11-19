namespace BlueHeighliner.Bundler;

/// <summary>
/// Downloads Python packages and everything they need as wheels for the target system's Python, so they install later with no network.
/// </summary>
internal interface IPythonWheels
{
    /// <summary>
    /// Downloads the packages with their dependencies and reads the license of every wheel.
    /// </summary>
    /// <param name="directory">The folder for the wheels.</param>
    /// <param name="requirements">What to install, such as <c>chromadb</c>.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>Every wheel in the folder, in name order.</returns>
    /// <exception cref="InvalidOperationException">pip is missing on this computer, or a package has no wheel for the target.</exception>
    Task<IReadOnlyList<PythonWheel>> Download(string directory, IReadOnlyList<string> requirements, CancellationToken cancellation = default);

    /// <summary>
    /// Writes the licenses as tab separated lines: name, version, status and license.
    /// </summary>
    /// <param name="wheels">The wheels.</param>
    /// <returns>The text.</returns>
    string Format(IEnumerable<PythonWheel> wheels);
}

/// <inheritdoc />
internal sealed class PythonWheels(IProcessRunner runner, INuGetLicenses licenses, IResources resources, ILog log) : IPythonWheels
{
    private readonly string pythonVersion = "3.12";

    private readonly string[] platforms = [.. Enumerable.Range(17, 23).Select(minor => $"manylinux_2_{minor}_x86_64"), "manylinux2014_x86_64", "manylinux2010_x86_64", "manylinux1_x86_64", "linux_x86_64"];

    private readonly Dictionary<string, string> spdx = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apache 2.0"] = "Apache-2.0",
        ["apache-2.0"] = "Apache-2.0",
        ["apache license, version 2.0"] = "Apache-2.0",
        ["apache license version 2.0"] = "Apache-2.0",
        ["apache software license"] = "Apache-2.0",
        ["mit"] = "MIT",
        ["mit license"] = "MIT",
        ["bsd license"] = "BSD-3-Clause",
        ["3-clause bsd license"] = "BSD-3-Clause",
        ["isc license"] = "ISC",
        ["isc license (iscl)"] = "ISC",
        ["python software foundation license"] = "PSF-2.0",
        ["mozilla public license 2.0 (mpl 2.0)"] = "MPL-2.0",
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<PythonWheel>> Download(string directory, IReadOnlyList<string> requirements, CancellationToken cancellation = default)
    {
        if (!(await runner.Try("python3", ["-m", "pip", "--version"])).Success)
        {
            throw new InvalidOperationException("pip is not available on this computer: install the python3-pip package to build this bundle");
        }

        Directory.CreateDirectory(directory);
        List<string> arguments = ["-m", "pip", "download", "--only-binary=:all:", "--disable-pip-version-check", "--python-version", pythonVersion, "--implementation", "cp", "--abi", "cp" + pythonVersion.Replace(".", string.Empty), "--abi", "abi3", "--abi", "none"];
        foreach (string platform in platforms)
        {
            arguments.AddRange(["--platform", platform]);
        }

        arguments.AddRange(["-d", directory, .. requirements]);
        log.Step($"Downloading {string.Join(' ', requirements)} and what it needs as wheels for Python {pythonVersion}");
        await runner.Run(new() { FileName = "python3", Arguments = arguments, StreamOutput = false }, cancellation);

        Dictionary<string, string> reviewed = Reviewed();
        List<PythonWheel> wheels = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.whl"))
        {
            wheels.Add(Read(path, reviewed));
        }

        log.Info($"{wheels.Count} wheels");
        return [.. wheels.OrderBy(wheel => wheel.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public string Format(IEnumerable<PythonWheel> wheels)
        => string.Concat(wheels.Select(wheel => $"{wheel.Name}\t{wheel.Version}\t{(wheel.NeedsReview ? "REVIEW" : "OK")}\t{wheel.License}\n"));

    private Dictionary<string, string> Reviewed()
    {
        Dictionary<string, string> reviewed = [];
        foreach (string line in resources.Text("reviewed-python.tsv").Split('\n'))
        {
            string[] fields = line.Split('\t');
            if (!line.StartsWith('#') && fields.Length >= 2)
            {
                reviewed[fields[0].ToLowerInvariant()] = fields[1];
            }
        }

        return reviewed;
    }

    private PythonWheel Read(string path, Dictionary<string, string> reviewed)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = zip.Entries.First(item => item.FullName.EndsWith(".dist-info/METADATA", StringComparison.Ordinal));
        using StreamReader reader = new(entry.Open());
        List<string> headers = [];
        for (string? line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
        {
            headers.Add(line);
        }

        string Header(string name)
            => headers.FirstOrDefault(line => line.StartsWith(name + ": ", StringComparison.Ordinal))?[(name.Length + 2)..].Trim() ?? string.Empty;

        string name = Header("Name");
        string expression = Header("License-Expression");
        string license = Header("License");
        string classifier = string.Join(" AND ", headers.Where(line => line.StartsWith("Classifier: License :: ", StringComparison.Ordinal)).Select(line => line.Split("::").Last().Trim()).Select(Normalize).Distinct());
        string result = expression.Length > 0 ? expression : license.Length > 0 && !license.Contains('\n') ? Normalize(license) : classifier.Length > 0 ? classifier : "unknown";
        bool permissive = licenses.IsPermissive(result);
        bool isReviewed = !permissive && reviewed.TryGetValue(name.ToLowerInvariant(), out _);
        return new() { Name = name, Version = Header("Version"), FileName = Path.GetFileName(path), License = isReviewed ? reviewed[name.ToLowerInvariant()] + " (reviewed)" : result, NeedsReview = !permissive && !isReviewed };
    }

    private string Normalize(string text)
    {
        string first = text.Split('\n')[0].Trim();
        return spdx.TryGetValue(first, out string? known) ? known : first;
    }
}
