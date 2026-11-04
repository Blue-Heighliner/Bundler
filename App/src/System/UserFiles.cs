namespace BlueHeighliner.Bundler;

/// <summary>
/// Writes files into a user's home folder as root while keeping them the user's own.
/// </summary>
internal interface IUserFiles
{
    /// <summary>
    /// Merges keys into a JSON settings file of a user, creating the file and its folders owned by the user.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="path">The settings file.</param>
    /// <param name="updates">The keys to set.</param>
    /// <param name="skipWhenPresent">When this key is already in the file, nothing is changed.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task SetJson(string user, string path, JsonObject updates, string? skipWhenPresent = null, CancellationToken cancellation = default);

    /// <summary>
    /// Removes the keys of <paramref name="expected" /> whose value is still the one given from a user's JSON settings file.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="path">The settings file.</param>
    /// <param name="expected">The keys and the values they were given.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the keys are removed.</returns>
    Task UnsetJson(string user, string path, JsonObject expected, CancellationToken cancellation = default);

    /// <summary>
    /// Writes a text file into a user's folders, creating them owned by the user.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="path">The file.</param>
    /// <param name="text">The content.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task Write(string user, string path, string text, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class UserFiles(IUsers users, IJsonFiles json) : IUserFiles
{
    /// <inheritdoc />
    public async Task SetJson(string user, string path, JsonObject updates, string? skipWhenPresent = null, CancellationToken cancellation = default)
    {
        await users.MakeDirectory(user, Path.GetDirectoryName(path)!, cancellation);
        json.Set(path, updates, skipWhenPresent);
        await users.Own(user, [path, path + ".bundler.bak"], cancellation);
    }

    /// <inheritdoc />
    public async Task UnsetJson(string user, string path, JsonObject expected, CancellationToken cancellation = default)
    {
        if (json.UnsetMatching(path, expected))
        {
            await users.Own(user, [path], cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Write(string user, string path, string text, CancellationToken cancellation = default)
    {
        await users.MakeDirectory(user, Path.GetDirectoryName(path)!, cancellation);
        await File.WriteAllTextAsync(path, text, cancellation);
        await users.Own(user, [path], cancellation);
    }
}
