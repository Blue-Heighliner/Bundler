namespace BlueHeighliner.Bundler;

/// <summary>
/// Adds and removes marked blocks of lines in shared files such as <c>/etc/environment</c>, so an uninstall takes out
/// exactly the lines it put in.
/// </summary>
internal interface ITextBlocks
{
    /// <summary>
    /// Writes a block at the end of a file, replacing a block with the same tag.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="tag">Names the block.</param>
    /// <param name="lines">The lines inside the block.</param>
    void Add(string path, string tag, IEnumerable<string> lines);

    /// <summary>
    /// Takes a block out of a file.
    /// </summary>
    /// <param name="path">The file; nothing happens when it does not exist.</param>
    /// <param name="tag">Names the block.</param>
    void Remove(string path, string tag);
}

/// <inheritdoc />
internal sealed class TextBlocks : ITextBlocks
{
    /// <inheritdoc />
    public void Add(string path, string tag, IEnumerable<string> lines)
    {
        Remove(path, tag);
        List<string> content = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        content.Add($"# bundler {tag} begin");
        content.AddRange(lines);
        content.Add($"# bundler {tag} end");
        File.WriteAllLines(path, content);
    }

    /// <inheritdoc />
    public void Remove(string path, string tag)
    {
        if (!File.Exists(path))
        {
            return;
        }

        List<string> kept = [];
        bool inside = false;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line == $"# bundler {tag} begin")
            {
                inside = true;
            }
            else if (inside)
            {
                inside = line != $"# bundler {tag} end";
            }
            else
            {
                kept.Add(line);
            }
        }

        File.WriteAllLines(path, kept);
    }
}
