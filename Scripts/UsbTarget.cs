#:package Markwardt.ScriptUtilities@0.2.0
#:property TreatWarningsAsErrors=true

// Rebuilds the system the installer USB drive puts on a computer, for testing bundles against exactly that and for refreshing the package
// list built into the app (App/src/Resources/target/dpkg-status.txt). Needs sudo, squashfs support and the drive mounted. Everything it
// does as root stays under /var/tmp/usb-target.
//
//   dotnet run Scripts/UsbTarget.cs create ISO_FOLDER   unpack the drive's system layers into /var/tmp/usb-target/pristine and install the
//                                                       kernel the installer adds (from the drive's own package pool, no network)
//   dotnet run Scripts/UsbTarget.cs status              write the trimmed package list of the pristine system into the app's resources
//   dotnet run Scripts/UsbTarget.cs reset               make /var/tmp/usb-target/work an exact copy of the pristine system (a clean start for a test)
//   dotnet run Scripts/UsbTarget.cs destroy             delete everything
//
// Test a bundle in the work copy with BUNDLER_TEST_ROOT=/var/tmp/usb-target/work and Scripts/Chroot.cs (sync, run).

using Markwardt.ScriptUtilities;

string root = "/var/tmp/usb-target";
string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
string command = arguments.Length > 0 ? arguments[0] : string.Empty;

switch (command)
{
    case "create" when arguments.Length > 1:
        string drive = Path.GetFullPath(arguments[1]);
        if (!File.Exists(Path.Combine(drive, "casper", "minimal.standard.en.squashfs")))
        {
            Script.Error($"{drive} is not the installer drive: no casper/minimal.standard.en.squashfs");
            return 1;
        }

        string script = """
            set -e
            drive=$1; base=$2; target=$base/pristine
            mkdir -p "$target" "$base/mnt/a" "$base/mnt/b" "$base/mnt/c" "$base/mnt/merged"
            mount -o loop,ro "$drive/casper/minimal.squashfs" "$base/mnt/a"
            mount -o loop,ro "$drive/casper/minimal.standard.squashfs" "$base/mnt/b"
            mount -o loop,ro "$drive/casper/minimal.standard.en.squashfs" "$base/mnt/c"
            trap 'umount "$base/mnt/merged" "$base/mnt/c" "$base/mnt/b" "$base/mnt/a" 2>/dev/null || true' EXIT
            mount -t overlay overlay -o lowerdir="$base/mnt/c:$base/mnt/b:$base/mnt/a" "$base/mnt/merged"
            cp -a "$base/mnt/merged/." "$target/"
            umount "$base/mnt/merged"
            mkdir -p "$target/cdrom"
            mount --bind "$drive" "$target/cdrom"
            mount -t proc proc "$target/proc"; mount -t sysfs sysfs "$target/sys"; mount --bind /dev "$target/dev"; mount -t tmpfs tmpfs "$target/run"
            trap 'umount "$target/cdrom" "$target/run" "$target/dev" "$target/sys" "$target/proc" 2>/dev/null || true; umount "$base/mnt/c" "$base/mnt/b" "$base/mnt/a" 2>/dev/null || true' EXIT
            printf '#!/bin/sh\nexit 101\n' > "$target/usr/sbin/policy-rc.d"; chmod +x "$target/usr/sbin/policy-rc.d"
            mkdir -p "$target/etc/apt/off"; mv "$target"/etc/apt/sources.list.d/* "$target/etc/apt/off/" 2>/dev/null || true; mv "$target/etc/apt/sources.list" "$target/etc/apt/off/" 2>/dev/null || true
            echo "deb [trusted=yes] file:///cdrom noble main restricted" > "$target/etc/apt/sources.list"
            chroot "$target" apt-get -q -o Acquire::Languages=none update
            chroot "$target" env DEBIAN_FRONTEND=noninteractive apt-get -q -y -o Dpkg::Options::=--force-confold install linux-generic-hwe-24.04
            chroot "$target" env DEBIAN_FRONTEND=noninteractive dpkg --configure -a
            """;
        (await Script.Run(true, "sudo", "bash", "-c", script, "_", drive, root)).Verify();
        return 0;
    case "status":
        string status = Path.Combine(root, "pristine", "var", "lib", "dpkg", "status");
        string[] keep = ["Package", "Status", "Priority", "Essential", "Architecture", "Multi-Arch", "Version", "Provides", "Depends", "Pre-Depends", "Conflicts", "Breaks", "Replaces", "Source"];
        List<string> paragraphs = [];
        foreach (string paragraph in File.ReadAllText(status).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Dictionary<string, string> fields = [];
            string? current = null;
            foreach (string line in paragraph.Split('\n'))
            {
                if (line.StartsWith(' ') || line.StartsWith('\t'))
                {
                    if (current is not null && fields.ContainsKey(current))
                    {
                        fields[current] += "\n" + line;
                    }
                }
                else if (line.IndexOf(':') is int colon and > 0 && keep.Contains(line[..colon]))
                {
                    current = line[..colon];
                    fields[current] = line[(colon + 1)..].Trim();
                }
                else
                {
                    current = null;
                }
            }

            if (fields.GetValueOrDefault("Status") == "install ok installed")
            {
                paragraphs.Add(string.Join('\n', keep.Where(fields.ContainsKey).Select(key => $"{key}: {fields[key]}")));
            }
        }

        File.WriteAllText("App/src/Resources/target/dpkg-status.txt", string.Join("\n\n", paragraphs) + "\n");
        Console.WriteLine($"{paragraphs.Count} packages written");
        return 0;
    case "reset":
        (await Script.Run(true, "sudo", "mkdir", "-p", $"{root}/work")).Verify();
        (await Script.Run(true, "sudo", "rsync", "-aHAX", "--delete", "--exclude=/srv/bundler", $"{root}/pristine/", $"{root}/work/")).Verify();
        return 0;
    case "destroy":
        (await Script.Run(true, "sudo", "rm", "-rf", "--one-file-system", root)).Verify();
        return 0;
    default:
        Script.Error("usage: UsbTarget.cs create ISO_FOLDER | status | reset | destroy");
        return 2;
}
