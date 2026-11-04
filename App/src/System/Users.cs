namespace BlueHeighliner.Bundler;

/// <summary>
/// The people who use the computer, and the work done for them: VS Code settings, extensions, templates and so on live in
/// each user's own home folder.
/// </summary>
internal interface IUsers
{
    /// <summary>
    /// Lists the accounts that belong to people: UID 1000 to 59999, with a login shell and a home folder under <c>/home</c> that exists.
    /// </summary>
    /// <returns>The account names.</returns>
    IReadOnlyList<string> Normal();

    /// <summary>
    /// Finds the home folder of an account.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <returns>The home folder.</returns>
    string Home(string user);

    /// <summary>
    /// Creates a folder and any missing parents, owned by the user.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="directory">The folder.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the folders exist.</returns>
    Task MakeDirectory(string user, string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Gives files to a user.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="paths">The files; ones that do not exist are skipped.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>A task that completes when the owner is changed.</returns>
    Task Own(string user, IEnumerable<string> paths, CancellationToken cancellation = default);

    /// <summary>
    /// Runs a program as a user with that user's home folder, from the root folder.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="check">Whether a non-zero exit code throws.</param>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellation">Stops the program when cancelled.</param>
    /// <returns>The result.</returns>
    Task<ProcessResult> RunAs(string user, bool check, string fileName, IReadOnlyList<string> arguments, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Users(IProcessRunner runner, ILog log) : IUsers
{
    /// <inheritdoc />
    public IReadOnlyList<string> Normal()
    {
        List<string> users = [];
        foreach (string line in File.ReadLines("/etc/passwd"))
        {
            string[] fields = line.Split(':');
            if (fields.Length >= 7 && int.TryParse(fields[2], out int uid) && uid is >= 1000 and < 60000
                && !fields[6].EndsWith("nologin", StringComparison.Ordinal) && !fields[6].EndsWith("false", StringComparison.Ordinal)
                && fields[5].StartsWith("/home/", StringComparison.Ordinal) && Directory.Exists(fields[5]))
            {
                users.Add(fields[0]);
            }
        }

        return users;
    }

    /// <inheritdoc />
    public string Home(string user)
    {
        foreach (string line in File.ReadLines("/etc/passwd"))
        {
            string[] fields = line.Split(':');
            if (fields.Length >= 6 && fields[0] == user)
            {
                return fields[5];
            }
        }

        throw new InvalidOperationException($"no such user: {user}");
    }

    /// <inheritdoc />
    public async Task MakeDirectory(string user, string directory, CancellationToken cancellation = default)
    {
        List<string> missing = [];
        string? current = directory;
        while (current is not null && !Directory.Exists(current))
        {
            missing.Insert(0, current);
            current = Path.GetDirectoryName(current);
        }

        foreach (string folder in missing)
        {
            Directory.CreateDirectory(folder);
            await Own(user, [folder], cancellation);
        }
    }

    /// <inheritdoc />
    public async Task Own(string user, IEnumerable<string> paths, CancellationToken cancellation = default)
    {
        string[] existing = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (existing.Length > 0)
        {
            await runner.Run(new() { FileName = "chown", Arguments = ["-h", $"{user}:", .. existing] }, cancellation);
        }
    }

    /// <inheritdoc />
    public Task<ProcessResult> RunAs(string user, bool check, string fileName, IReadOnlyList<string> arguments, CancellationToken cancellation = default)
    {
        log.Info($"as {user}: {fileName} {string.Join(' ', arguments)}");
        return runner.Run(
            new()
            {
                FileName = "runuser",
                Arguments = ["-u", user, "--", "env", $"HOME={Home(user)}", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_NOLOGO=1", fileName, .. arguments],
                WorkingDirectory = "/",
                Check = check,
            },
            cancellation);
    }
}
