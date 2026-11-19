namespace BlueHeighliner.Bundler;

/// <summary>
/// The steps that remove what an install put on the computer, built when the bundle is installed and written next to its install records as a
/// self-contained bash script. An uninstall runs that script, so it does what the installed version of the bundle needs and depends on neither
/// the bundle file that is available nor the version of Bundler that runs it.
/// </summary>
internal sealed class UninstallScript
{
    private static string Quote(string text)
        => "'" + text.Replace("'", "'\\''") + "'";

    /// <summary>Gets the steps in the order they run.</summary>
    public List<UninstallStep> Steps { get; } = [];

    /// <summary>
    /// Writes the script as bash: a header, the helper functions and one call for each step.
    /// </summary>
    /// <param name="bundle">The name of the bundle, for the header.</param>
    /// <param name="prelude">The helper functions the steps call (<c>uninstall/prelude.sh</c>).</param>
    /// <returns>The text of the script.</returns>
    public string Render(string bundle, string prelude)
    {
        StringBuilder text = new();
        text.Append("#!/bin/bash\n# Removes what the install of the ").Append(bundle).Append(" bundle put on this computer. Written by Bundler when it was installed; run by Bundler to uninstall.\n\n");
        text.Append(prelude.TrimEnd()).Append("\n\n");
        Write(text, Steps, string.Empty);
        return text.ToString();
    }

    /// <summary>
    /// Adds a step.
    /// </summary>
    /// <param name="name">The action.</param>
    /// <param name="args">Its arguments.</param>
    public void Add(string name, params IEnumerable<string> args)
        => Steps.Add(new() { Name = name, Args = [.. args] });

    /// <summary>
    /// Adds steps that run only when one of the folders holds a file, or when none does.
    /// </summary>
    /// <param name="anyFile"><see langword="true" /> to run when some file is there, <see langword="false" /> to run when none is.</param>
    /// <param name="folders">The folders to look in, with their subfolders.</param>
    /// <param name="then">Fills the guarded steps.</param>
    public void IfFiles(bool anyFile, IEnumerable<string> folders, Action<UninstallScript> then)
    {
        UninstallScript inner = new();
        then(inner);
        Steps.Add(new() { Name = anyFile ? "if-files" : "if-no-files", Args = [.. folders], Then = inner.Steps });
    }

    /// <summary>
    /// Adds steps that run only when systemd runs on the computer.
    /// </summary>
    /// <param name="then">Fills the guarded steps.</param>
    public void IfSystemd(Action<UninstallScript> then)
    {
        UninstallScript inner = new();
        then(inner);
        Steps.Add(new() { Name = "if-systemd", Then = inner.Steps });
    }

    /// <summary>Adds a step that deletes a file when it is there.</summary>
    /// <param name="path">The file.</param>
    public void Remove(string path)
        => Add("rm", path);

    /// <summary>Adds a step that deletes a folder with everything in it.</summary>
    /// <param name="path">The folder.</param>
    public void RemoveTree(string path)
        => Add("rm-tree", path);

    /// <summary>Adds a step that deletes a folder when it is empty.</summary>
    /// <param name="path">The folder.</param>
    public void RemoveEmpty(string path)
        => Add("rm-empty", path);

    /// <summary>Adds a step that deletes a symbolic link, only when it points into a place.</summary>
    /// <param name="path">The link.</param>
    /// <param name="prefix">The target it must be or lie under, or <see langword="null" /> for any target.</param>
    public void RemoveLink(string path, string? prefix = null)
        => Add("rm-link", prefix is null ? [path] : [path, prefix]);

    /// <summary>Adds a step that writes a line to the log.</summary>
    /// <param name="message">The line.</param>
    public void Info(string message)
        => Add("info", message);

    /// <summary>Adds a step that writes a warning to the log.</summary>
    /// <param name="message">The warning.</param>
    public void Warn(string message)
        => Add("warn", message);

    private void Write(StringBuilder text, IEnumerable<UninstallStep> steps, string indent)
    {
        foreach (UninstallStep step in steps)
        {
            string call = "b_" + step.Name.Replace('-', '_') + string.Concat(step.Args.Select(arg => " " + Quote(arg)));
            if (step.Then.Count == 0)
            {
                text.Append(indent).Append(call).Append('\n');
                continue;
            }

            string condition = step.Name switch
            {
                "if-no-files" => "! b_has_files" + string.Concat(step.Args.Select(arg => " " + Quote(arg))),
                "if-files" => "b_has_files" + string.Concat(step.Args.Select(arg => " " + Quote(arg))),
                _ => "b_systemd_running",
            };
            text.Append(indent).Append("if ").Append(condition).Append("; then\n");
            Write(text, step.Then, indent + "    ");
            text.Append(indent).Append("fi\n");
        }
    }
}
