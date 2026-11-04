namespace BlueHeighliner.Bundler;

/// <summary>
/// Edits JSON settings files such as VS Code's <c>settings.json</c>, which may hold comments and trailing commas.
/// </summary>
internal interface IJsonFiles
{
    /// <summary>
    /// Merges keys into a file, creating it when it does not exist. A file that already has the values is not rewritten.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="updates">The keys and values to set.</param>
    /// <param name="skipWhenPresent">When this key is already in the file, nothing is changed.</param>
    /// <returns><see langword="true" /> when the file was written.</returns>
    /// <exception cref="InvalidOperationException">The file exists but is not a JSON object; it is left alone.</exception>
    bool Set(string path, JsonObject updates, string? skipWhenPresent = null);

    /// <summary>
    /// Removes the keys of <paramref name="expected" /> whose current value is still the one given, and deletes the file when
    /// nothing is left in it.
    /// </summary>
    /// <param name="path">The file; nothing happens when it does not exist.</param>
    /// <param name="expected">The keys and the values they were given.</param>
    /// <returns><see langword="true" /> when the file was changed.</returns>
    bool UnsetMatching(string path, JsonObject expected);
}

/// <inheritdoc />
internal sealed class JsonFiles : IJsonFiles
{
    private static readonly JsonDocumentOptions lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly JsonDocumentOptions strict = new();

    private static readonly JsonSerializerOptions output = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc />
    public bool Set(string path, JsonObject updates, string? skipWhenPresent = null)
    {
        JsonObject data = Load(path);
        if (skipWhenPresent is not null && data.ContainsKey(skipWhenPresent))
        {
            return false;
        }

        JsonObject merged = (JsonObject)data.DeepClone();
        foreach ((string key, JsonNode? value) in updates)
        {
            merged[key] = value?.DeepClone();
        }

        if (File.Exists(path) && JsonNode.DeepEquals(data, merged))
        {
            return false;
        }

        Save(path, merged);
        return true;
    }

    /// <inheritdoc />
    public bool UnsetMatching(string path, JsonObject expected)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        JsonObject data = Load(path);
        bool changed = false;
        foreach ((string key, JsonNode? value) in expected)
        {
            if (data.TryGetPropertyValue(key, out JsonNode? current) && JsonNode.DeepEquals(current, value))
            {
                data.Remove(key);
                changed = true;
            }
        }

        if (changed)
        {
            Save(path, data);
        }

        return changed;
    }

    private JsonObject Load(string path)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(File.ReadAllText(path)))
        {
            return new();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), null, lenient) as JsonObject ?? throw new InvalidOperationException($"{path} is not a JSON object; not changed");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{path} could not be parsed ({exception.Message}); not changed");
        }
    }

    private void Save(string path, JsonObject data)
    {
        if (data.Count == 0 && File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string backup = path + ".bundler.bak";
        if (File.Exists(path) && !File.Exists(backup) && !Parses(File.ReadAllText(path), strict))
        {
            File.Copy(path, backup);
        }

        File.WriteAllText(path, data.ToJsonString(output) + "\n");
    }

    private bool Parses(string text, JsonDocumentOptions options)
    {
        try
        {
            JsonNode.Parse(text, null, options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
