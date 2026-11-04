namespace BlueHeighliner.Bundler;

/// <summary>
/// Reads the files that are built into the executable: package lists, settings files, service files and the list of reviewed
/// licenses.
/// </summary>
internal interface IResources
{
    /// <summary>
    /// Reads an embedded text file.
    /// </summary>
    /// <param name="name">The path below <c>Resources/</c>, such as <c>dev/packages.txt</c>.</param>
    /// <returns>The text.</returns>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    string Text(string name);
}

/// <inheritdoc />
internal sealed class Resources : IResources
{
    /// <inheritdoc />
    public string Text(string name)
    {
        using Stream stream = typeof(Resources).Assembly.GetManifestResourceStream("Resources/" + name)
            ?? throw new FileNotFoundException($"embedded file {name} not found");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
