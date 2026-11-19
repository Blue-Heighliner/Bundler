namespace BlueHeighliner.Bundler.Tests;

internal sealed class UninstallHarness : IDisposable
{
    private static readonly string log = "echo \"$(basename \"$0\") $*\" >> \"$B_TEST_CALLS\"\n";

    private readonly TempDirectory directory = new();

    public UninstallHarness()
    {
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Path.Combine(World, "needed"));
        foreach (string name in new[] { "apt-mark", "systemctl", "runuser", "userdel", "usermod", "chown", "update-desktop-database" })
        {
            Fake(name, string.Empty);
        }

        Fake("loginctl", "[ -e \"$B_TEST_WORLD/loginctl-fails\" ] && exit 1\n");
        Fake("dpkg-query", "for last; do :; done\ngrep -qxF -- \"$last\" \"$B_TEST_WORLD/installed\" 2>/dev/null && echo installed || echo not-installed\nexit 0\n", logged: false);
        Fake(
            "apt-get",
            """
            if [ "$1" = "-s" ]; then
                case " $* " in
                    *" autoremove "*)
                        for name in $(cat "$B_TEST_WORLD/removable" 2>/dev/null); do
                            grep -qxF -- "$name" "$B_TEST_WORLD/installed" 2>/dev/null && echo "Remv $name [1.0]"
                        done ;;
                    *" remove "*)
                        for last; do :; done
                        echo "Remv $last [1.0]"
                        for name in $(cat "$B_TEST_WORLD/needed/$last" 2>/dev/null); do echo "Remv $name [1.0]"; done ;;
                esac
                exit 0
            fi
            if [ "$1" = "remove" ]; then
                shift 3
                for name in "$@"; do
                    grep -vxF -- "$name" "$B_TEST_WORLD/installed" > "$B_TEST_WORLD/installed.tmp"
                    mv "$B_TEST_WORLD/installed.tmp" "$B_TEST_WORLD/installed"
                done
            fi
            exit 0
            """,
            logged: false);
        Fake(
            "git",
            """
            if [ "$1 $2" = "config --system" ]; then
                case "$3" in
                    --get) cat "$B_TEST_WORLD/git-$4" 2>/dev/null; exit 0 ;;
                    --unset) rm -f "$B_TEST_WORLD/git-$4" ;;
                esac
            fi
            """);
    }

    public string Root => directory.Path;

    public string Bin => directory.Combine("bin");

    public string World => directory.Combine("world");

    public IReadOnlyList<string> Calls => File.Exists(directory.Combine("calls.log")) ? File.ReadAllLines(directory.Combine("calls.log")) : [];

    public void Installed(params string[] packages)
        => File.AppendAllLines(Path.Combine(World, "installed"), packages);

    public IReadOnlyList<string> InstalledNow
        => File.Exists(Path.Combine(World, "installed")) ? File.ReadAllLines(Path.Combine(World, "installed")).Where(line => line.Length > 0).ToList() : [];

    public void Removable(params string[] packages)
        => File.AppendAllLines(Path.Combine(World, "removable"), packages);

    public void NeededBy(string package, params string[] names)
        => File.WriteAllLines(Path.Combine(World, "needed", package), names);

    public void Git(string key, string value)
        => File.WriteAllText(Path.Combine(World, "git-" + key), value + "\n");

    public bool HasGit(string key)
        => File.Exists(Path.Combine(World, "git-" + key));

    public string Prelude()
        => new Resources().Text("uninstall/prelude.sh");

    public async Task<string> Run(IBundleState state, UninstallScript script, string bundle = "test")
    {
        string path = state.PathOf("uninstall.sh");
        Directory.CreateDirectory(state.Directory);
        await File.WriteAllTextAsync(path, script.Render(bundle, Prelude()));
        return await Execute(path);
    }

    public async Task<string> Execute(string path)
    {
        ProcessStartInfo info = new("bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(path);
        info.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        info.Environment["B_TEST_CALLS"] = directory.Combine("calls.log");
        info.Environment["B_TEST_WORLD"] = World;
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string text = await output + await error;
        Assert.True(process.ExitCode == 0, $"the script exited with {process.ExitCode}: {text}");
        return text;
    }

    public void Dispose()
        => directory.Dispose();

    private void Fake(string name, string body, bool logged = true)
    {
        string path = Path.Combine(Bin, name);
        File.WriteAllText(path, "#!/bin/bash\n" + (logged ? log : string.Empty) + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
