#:package Markwardt.ScriptUtilities@0.2.0
#:property TreatWarningsAsErrors=true

// Test harness: runs the published app inside a throwaway Ubuntu 26.04 chroot, so installs can be tried without touching this
// computer. Needs sudo and mmdebstrap. Everything it does as root stays inside the chroot folder under /var/tmp or /tmp.
//
//   dotnet run Scripts/Chroot.cs create                 build the chroot (once, about a minute)
//   dotnet run Scripts/Chroot.cs sync [BUNDLE...]       copy the published app and the file of each given bundle (hard links when the disks match, otherwise copies, taken from
//                                                       the repo's Bundles/ folder) into the chroot, next to the app in /srv/bundler
//   dotnet run Scripts/Chroot.cs run 'COMMAND'          run a shell command in the chroot as root, in /srv/bundler
//   dotnet run Scripts/Chroot.cs user 'COMMAND'         the same as the test user "tester"
//   dotnet run Scripts/Chroot.cs destroy                delete the chroot
//
// BUNDLER_TEST_PUBLISH is the folder `dotnet publish App/App.csproj -r linux-x64 -o <folder>` wrote (default /tmp/bundler-publish).
// BUNDLER_TEST_NONET=1 runs commands without any network, like the real target.

using Markwardt.ScriptUtilities;

string root = Environment.GetEnvironmentVariable("BUNDLER_TEST_ROOT") ?? "/var/tmp/bundler-test/rootfs";
if (!root.StartsWith("/var/tmp/") && !root.StartsWith("/tmp/"))
{
    Script.Error($"refusing to use {root}: the test chroot must be under /var/tmp or /tmp");
    return 1;
}

string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
string command = arguments.Length > 0 ? arguments[0] : string.Empty;

switch (command)
{
    case "create":
        (await Script.Run(true, "sudo", "mkdir", "-p", Path.GetDirectoryName(root)!)).Verify();
        (await Script.Run(true, "sudo", "mmdebstrap", "--variant=important", "--include=apt,passwd,util-linux,debconf,sudo,xauth,ca-certificates,polkitd", "resolute", root, "http://archive.ubuntu.com/ubuntu")).Verify();
        (await Enter("useradd -m -s /bin/bash tester; id tester", "root")).Verify();
        return 0;
    case "sync":
        string publish = Environment.GetEnvironmentVariable("BUNDLER_TEST_PUBLISH") ?? "/tmp/bundler-publish";
        string target = $"{root}/srv/bundler/Bundles";
        string source = "Bundles";
        (await Script.Run(true, "sudo", "mkdir", "-p", target, $"{root}/srv/bundler")).Verify();
        (await Script.Run(true, "sudo", "sh", "-c", $"rm -f '{target}'/*.bundle; cp '{publish}/Bundler' '{root}/srv/bundler/Bundler'; chmod 755 '{root}/srv/bundler/Bundler'")).Verify();
        foreach (string name in arguments.Skip(1))
        {
            string? newest = Path.Combine(source, $"{name}.bundle") is { } path && File.Exists(path) ? path : null;
            if (newest is null)
            {
                Script.Error($"no built file for {name} in {source}");
                return 1;
            }

            (await Script.Run(true, "sudo", "sh", "-c", "ln \"$1\" \"$2\" 2>/dev/null || cp \"$1\" \"$2\"", "_", Path.GetFullPath(newest), $"{target}/{Path.GetFileName(newest)}")).Verify();
        }

        return 0;
    case "run" when arguments.Length > 1:
        return (await Enter(arguments[1], "root")).ExitCode;
    case "user" when arguments.Length > 1:
        return (await Enter(arguments[1], "tester")).ExitCode;
    case "destroy":
        (await Script.Run(true, "sudo", "rm", "-rf", "--one-file-system", root)).Verify();
        return 0;
    default:
        Script.Error("usage: Chroot.cs create | sync [BUNDLE...] | run 'COMMAND' | user 'COMMAND' | destroy");
        return 2;
}

async Task<RunResult> Enter(string shellCommand, string user)
{
    string[] network = Environment.GetEnvironmentVariable("BUNDLER_TEST_NONET") == "1" ? ["-n"] : [];
    string script = """
        root=$1; cmd=$2; user=${3:-root}
        mount -t proc proc "$root/proc"
        mkdir -p "$root/sys"; mount -t sysfs sysfs "$root/sys" 2>/dev/null || true
        mkdir -p "$root/dev/shm" "$root/dev/pts" "$root/run"; mount -t tmpfs tmpfs "$root/dev/shm"; mount -t tmpfs tmpfs "$root/run"
        mount -t devpts devpts "$root/dev/pts" 2>/dev/null || true
        ip link set lo up 2>/dev/null || true
        [ -e "$root/dev/null" ] || mknod -m 666 "$root/dev/null" c 1 3
        if [ "$user" = root ]; then
          exec chroot "$root" /usr/bin/env -i HOME=/root TERM=xterm PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin /bin/bash -c "cd /srv/bundler 2>/dev/null; $cmd"
        else
          exec chroot "$root" /usr/sbin/runuser -u "$user" -- /usr/bin/env -i HOME=/home/$user TERM=xterm PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin /bin/bash -c "cd /srv/bundler 2>/dev/null; $cmd"
        fi
        """;
    return await Script.Run(true, "sudo", ["unshare", "-m", "-p", "-f", .. network, "--kill-child", "bash", "-c", script, "_", root, shellCommand, user]);
}
