namespace BlueHeighliner.Bundler;

/// <summary>
/// What is recorded about one installed bundle in <c>/var/lib/bundler/&lt;bundle&gt;/</c>, so an uninstall undoes exactly what the
/// install did.
/// </summary>
internal interface IBundleState
{
    /// <summary>Gets the folder of this bundle's records.</summary>
    string Directory { get; }

    /// <summary>Gets a value indicating whether an install of this bundle finished.</summary>
    bool IsInstalled { get; }

    /// <summary>Gets the accounts the bundle was installed for.</summary>
    IReadOnlyList<string> Users { get; }

    /// <summary>
    /// Records that an install finished.
    /// </summary>
    /// <param name="users">The accounts that were set up.</param>
    void Begin(IEnumerable<string> users);

    /// <summary>Forgets the bundle.</summary>
    void End();

    /// <summary>
    /// Records that the bundle installed or replaced a component itself, so only then does an uninstall remove it.
    /// </summary>
    /// <param name="key">Names the component.</param>
    void MarkOwned(string key);

    /// <summary>
    /// Asks whether the bundle installed a component itself.
    /// </summary>
    /// <param name="key">Names the component.</param>
    /// <returns><see langword="true" /> when it did.</returns>
    bool IsOwned(string key);

    /// <summary>
    /// Gives the path of a record file.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The full path.</returns>
    string PathOf(string name);

    /// <summary>
    /// Reads the lines of a record file.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The lines, none when the file does not exist.</returns>
    IReadOnlyList<string> Lines(string name);

    /// <summary>
    /// Adds a line to a record file unless it already has it.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="line">The line.</param>
    void Add(string name, string line);

    /// <summary>
    /// Replaces the content of a record file.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="text">The text.</param>
    void Write(string name, string text);

    /// <summary>
    /// Reads a record file as text.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The text, or <see langword="null" /> when the file does not exist.</returns>
    string? Read(string name);
}

/// <inheritdoc />
internal sealed class BundleState(string directory) : IBundleState
{
    /// <inheritdoc />
    public string Directory { get; } = directory;

    /// <inheritdoc />
    public bool IsInstalled => File.Exists(PathOf("installed"));

    /// <inheritdoc />
    public IReadOnlyList<string> Users => (Field("users") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <inheritdoc />
    public void Begin(IEnumerable<string> users)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathOf("installed"), $"installed={DateTimeOffset.Now:O}\nusers={string.Join(' ', users)}\n");
    }

    /// <inheritdoc />
    public void End()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    /// <inheritdoc />
    public void MarkOwned(string key)
        => Write($"owned-{key}", string.Empty);

    /// <inheritdoc />
    public bool IsOwned(string key)
        => File.Exists(PathOf($"owned-{key}"));

    /// <inheritdoc />
    public string PathOf(string name)
        => Path.Combine(Directory, name);

    /// <inheritdoc />
    public IReadOnlyList<string> Lines(string name)
        => File.Exists(PathOf(name)) ? File.ReadAllLines(PathOf(name)).Where(line => line.Length > 0).ToList() : [];

    /// <inheritdoc />
    public void Add(string name, string line)
    {
        if (!Lines(name).Contains(line))
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(PathOf(name), line + "\n");
        }
    }

    /// <inheritdoc />
    public void Write(string name, string text)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathOf(name), text);
    }

    /// <inheritdoc />
    public string? Read(string name)
        => File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : null;

    private string? Field(string key)
        => Lines("installed").Where(line => line.StartsWith(key + "=", StringComparison.Ordinal)).Select(line => line[(key.Length + 1)..]).FirstOrDefault();
}
