namespace BlueHeighliner.Bundler;

/// <summary>
/// Removes package references from a dotnet template pack, so the projects it creates do not need those packages.
/// </summary>
internal interface ITemplatePatcher
{
    /// <summary>
    /// Edits a template pack in place. Every <c>PackageReference</c> and <c>PackageVersion</c> element for the given ids is taken
    /// out of the template's project files (<c>.csproj</c>, <c>.fsproj</c>, <c>.vbproj</c>, <c>.props</c>, <c>.targets</c>). The lines that
    /// call the given methods (which come from the removed packages) are taken out of the template's source files, together
    /// with any <c>#if DEBUG</c> block and template condition markers that this leaves empty. The repository signature, which no
    /// longer fits, is dropped and a note is added to the description.
    /// </summary>
    /// <param name="nupkg">The template pack.</param>
    /// <param name="packageIds">The packages whose references are removed.</param>
    /// <param name="calls">The names of methods whose calls are removed.</param>
    /// <returns>How many references or calls were removed, by what was asked for.</returns>
    /// <exception cref="InvalidOperationException">Something that was asked for is not in the pack, which means the pack changed.</exception>
    IReadOnlyDictionary<string, int> Patch(string nupkg, IReadOnlyList<string> packageIds, IReadOnlyList<string> calls);
}

/// <inheritdoc />
internal sealed partial class TemplatePatcher : ITemplatePatcher
{
    private static readonly string[] projectFiles = [".csproj", ".fsproj", ".vbproj", ".props", ".targets"];

    private static readonly string[] codeFiles = [".cs", ".fs", ".vb"];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> Patch(string nupkg, IReadOnlyList<string> packageIds, IReadOnlyList<string> calls)
    {
        Dictionary<string, int> removed = packageIds.Concat(calls.Select(call => call + "()")).ToDictionary(name => name, _ => 0);
        string temporary = nupkg + ".tmp";
        using (ZipArchive source = ZipFile.OpenRead(nupkg))
        using (ZipArchive target = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                if (entry.FullName.EndsWith(".signature.p7s", StringComparison.Ordinal))
                {
                    continue;
                }

                byte[] data = Read(entry);
                string extension = Path.GetExtension(entry.FullName).ToLowerInvariant();
                if (projectFiles.Contains(extension))
                {
                    data = Rewrite(data, text =>
                    {
                        foreach (string id in packageIds)
                        {
                            (text, int count) = StripReferences(text, id);
                            removed[id] += count;
                        }

                        return text;
                    });
                }
                else if (calls.Count > 0 && codeFiles.Contains(extension))
                {
                    data = Rewrite(data, text =>
                    {
                        foreach (string call in calls)
                        {
                            (text, int count) = StripCalls(text, call);
                            removed[call + "()"] += count;
                        }

                        return text;
                    });
                }
                else if (entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && !entry.FullName.Contains('/'))
                {
                    string note = $" Modified by bundler: the references to {string.Join(", ", packageIds)} were removed from the project templates.";
                    data = Rewrite(data, text => DescriptionEnd().Replace(text, note + "</description>", 1));
                }

                ZipArchiveEntry created = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using Stream output = created.Open();
                output.Write(data);
            }
        }

        File.Move(temporary, nupkg, overwrite: true);
        string missing = removed.Where(pair => pair.Value == 0).Select(pair => pair.Key).FirstOrDefault() ?? string.Empty;
        return missing.Length == 0 ? removed : throw new InvalidOperationException($"{missing} was not found in {nupkg}: the template pack changed, check what it references now");
    }

    [GeneratedRegex("</description>")]
    private static partial Regex DescriptionEnd();

    [GeneratedRegex(@"^[ \t]*#if DEBUG[ \t]*\r?\n(?:[ \t]*\r?\n)*[ \t]*#endif[ \t]*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex EmptyDebugBlock();

    [GeneratedRegex(@"^[ \t]*//-:cnd:noEmit[ \t]*\r?\n(?:[ \t]*\r?\n)*[ \t]*//\+:cnd:noEmit[ \t]*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex EmptyConditionMarkers();

    private byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private byte[] Rewrite(byte[] data, Func<string, string> change)
    {
        bool bom = data.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        string text = Encoding.UTF8.GetString(data, bom ? 3 : 0, data.Length - (bom ? 3 : 0));
        byte[] body = Encoding.UTF8.GetBytes(change(text));
        return bom ? [.. Encoding.UTF8.GetPreamble(), .. body] : body;
    }

    private (string Text, int Count) StripReferences(string text, string id)
    {
        Regex pattern = new(
            $@"[ \t]*<Package(?:Reference|Version)\s+(?:Include|Update)=""{Regex.Escape(id)}""[^>]*?(?:/>|>.*?</Package(?:Reference|Version)>)[ \t]*\r?\n?",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        int count = pattern.Count(text);
        return (pattern.Replace(text, string.Empty), count);
    }

    private (string Text, int Count) StripCalls(string text, string call)
    {
        Regex pattern = new($@"^[^\n]*\b{Regex.Escape(call)}\(\)[^\n]*\n?", RegexOptions.Multiline);
        int count = pattern.Count(text);
        if (count == 0)
        {
            return (text, 0);
        }

        return (EmptyConditionMarkers().Replace(EmptyDebugBlock().Replace(pattern.Replace(text, string.Empty), string.Empty), string.Empty), count);
    }
}
