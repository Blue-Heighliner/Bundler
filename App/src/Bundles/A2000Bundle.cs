namespace BlueHeighliner.Bundler;

/// <summary>
/// The NVIDIA Linux driver for the RTX A2000 (Ampere): the newest series Ubuntu packages (up to the series NVIDIA lists as its latest stable release), as the packages
/// Ubuntu builds from it. The open kernel modules are prebuilt and signed by Canonical, so they load with Secure Boot on and
/// need no compiler. They are built for one kernel, so the bundle brings that kernel too.
/// </summary>
internal sealed class A2000Bundle(IReleaseSources sources, IAptBuilder apt, IAptInstaller installer, IDecisions decisions, IProcessRunner runner, IVersions versions, ILog log) : BundleBase
{
    private static readonly string release = "24.04";

    private static readonly string[] a2000Ids = ["2531", "2571"];

    /// <inheritdoc />
    public override string Name => "a2000";

    /// <inheritdoc />
    public override string Description => "Makes the NVIDIA RTX A2000 graphics card work for the display and for running AI models.";

    /// <inheritdoc />
    public override int Order => 15;

    /// <inheritdoc />
    public override async Task<BuildResult> Build(string directory, CancellationToken cancellation = default)
    {
        log.Step("Looking up NVIDIA's latest stable driver");
        string latest = await sources.LatestNvidiaDriver(cancellation);
        log.Info($"NVIDIA's latest stable release is {latest}");
        await apt.Prepare([], cancellation);
        string series = await Series(latest.Split('.')[0], cancellation);
        string driver = $"nvidia-driver-{series}-open";
        string modules = $"linux-modules-nvidia-{series}-open-generic";
        log.Info($"the newest series Ubuntu {release} packages is {series}");
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = [driver, modules] }, cancellation);
        string version = versions.Upstream(packages.First(package => package.Package == driver).Version, '-');
        if (version != latest)
        {
            log.Warn($"Ubuntu packages the NVIDIA {series} driver as {version}, NVIDIA's own latest is {latest}. The Ubuntu build is used.");
        }

        DebPackage modulesPackage = packages.First(package => package.Package == modules);
        List<string> components = [$"NVIDIA driver {version} ({driver})", $"{modulesPackage.Package} {modulesPackage.Version}"];
        if (packages.FirstOrDefault(package => package.Package.StartsWith("linux-image-", StringComparison.Ordinal)) is DebPackage kernel)
        {
            components.Add($"{kernel.Package} {kernel.Version} (the kernel the prebuilt modules match)");
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "version.txt"), version, cancellation);
        return new() { Components = components };
    }

    /// <inheritdoc />
    public override async Task Install(InstallContext context, CancellationToken cancellation = default)
    {
        log.Step("Graphics hardware");
        ReportGraphicsCards();

        log.Step("NVIDIA driver");
        string version = (await File.ReadAllTextAsync(Path.Combine(context.Payload, "version.txt"), cancellation)).Trim();
        string? have = await InstalledVersion(cancellation);
        if (decisions.Install("NVIDIA driver", have, version))
        {
            await RemoveOtherSeries(version.Split('.')[0], cancellation);
            await installer.InstallRecorded(Path.Combine(context.Payload, "debs"), context.State, cancellation);
            context.State.MarkOwned("driver");
        }

        string loaded = File.Exists("/sys/module/nvidia/version") ? File.ReadAllText("/sys/module/nvidia/version").Trim() : string.Empty;
        if (loaded != version)
        {
            log.Warn($"restart the computer to load the driver. The prebuilt modules are for kernel {ModulesKernel(context)}: if the computer starts a different kernel (a newer one, such as an HWE kernel), pick that one in the boot menu.");
        }

        ProcessResult cards = await runner.Try("nvidia-smi", ["-L"]);
        if (cards.Success && cards.Output.Contains("GPU"))
        {
            log.Info(cards.Text);
        }
    }

    /// <inheritdoc />
    public override async Task Uninstall(UninstallContext context, CancellationToken cancellation = default)
    {
        if (!context.State.IsOwned("driver"))
        {
            log.Info("the NVIDIA driver was already on this computer and was not installed by this bundle, so it is left in place");
        }

        await installer.RemoveAdded(context.State, cancellation);
        if (context.State.IsOwned("driver"))
        {
            log.Warn("restart the computer to unload the driver (the display falls back to the open nouveau driver).");
        }
    }

    private string ModulesKernel(InstallContext context)
    {
        string? package = context.Info.Components.FirstOrDefault(component => component.StartsWith("linux-modules-nvidia-", StringComparison.Ordinal));
        string version = package?.Split(' ').ElementAtOrDefault(1) ?? string.Empty;
        return version.Contains('.') ? version[..version.LastIndexOf('.')] + "-generic" : "of the bundle";
    }

    private async Task<string> Series(string latest, CancellationToken cancellation)
    {
        IReadOnlyList<string> offered = await apt.FindAll(@"nvidia-driver-[0-9]+-open", cancellation);
        foreach (string series in offered.Select(name => name["nvidia-driver-".Length..^"-open".Length]).Where(series => int.Parse(series) <= int.Parse(latest)).OrderByDescending(int.Parse))
        {
            if (await apt.Exists($"linux-modules-nvidia-{series}-open-generic", cancellation))
            {
                return series;
            }
        }

        throw new InvalidOperationException($"Ubuntu {release} has no packaged NVIDIA open driver up to the {latest} series");
    }

    private async Task RemoveOtherSeries(string series, CancellationToken cancellation)
    {
        ProcessResult result = await runner.Run(new() { FileName = "dpkg-query", Arguments = ["-W", "-f", "${db:Status-Status} ${Package}\n", "nvidia-driver-*", "nvidia-kernel-common-*", "nvidia-utils-*", "libnvidia-gl-*"], Check = false }, cancellation);
        IEnumerable<string> others = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')).Where(fields => fields.Length == 2 && fields[0] == "installed")
            .Select(fields => Regex.Match(fields[1], @"^(?:nvidia-driver|nvidia-kernel-common|nvidia-utils|libnvidia-gl)-([0-9]+)").Groups[1].Value).Where(other => other.Length > 0 && other != series).Distinct();
        foreach (string other in others)
        {
            log.Info($"the NVIDIA {other} driver is installed and the bundle brings the {series} series, which cannot be installed next to it: removing the {other} packages first");
            IReadOnlyList<string> removed = await installer.RemoveInstalled($@"(?:lib)?nvidia-[a-z0-9-]*-{other}(?:-.*)?|xserver-xorg-video-nvidia-{other}|linux-modules-nvidia-{other}-.*", cancellation);
            log.Info($"removed {string.Join(' ', removed)}");
        }
    }

    private async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        ProcessResult result = await runner.Run(new() { FileName = "dpkg-query", Arguments = ["-W", "-f", "${db:Status-Status} ${Package} ${Version}\n", "nvidia-driver-*"], Check = false }, cancellation);
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')).Where(fields => fields.Length == 3 && fields[0] == "installed")
            .Select(fields => versions.Upstream(fields[2], '-')).OrderBy(version => version, Comparer<string>.Create(versions.Compare)).LastOrDefault();
    }

    private void ReportGraphicsCards()
    {
        bool found = false;
        if (Directory.Exists("/sys/bus/pci/devices"))
        {
            foreach (string device in Directory.EnumerateDirectories("/sys/bus/pci/devices"))
            {
                if (Read(device, "vendor") == "0x10de" && Read(device, "class").StartsWith("0x03", StringComparison.Ordinal))
                {
                    string id = Read(device, "device").Replace("0x", string.Empty);
                    found = true;
                    log.Info(a2000Ids.Contains(id) ? $"NVIDIA RTX A2000 found ({device}, device id {id})" : $"NVIDIA GPU found, but it is not an RTX A2000 (device id {id})");
                }
            }
        }

        if (!found)
        {
            log.Warn("no NVIDIA graphics card was found on this computer. The driver is installed anyway and starts working when a card is fitted.");
        }
    }

    private string Read(string device, string file)
        => File.Exists(Path.Combine(device, file)) ? File.ReadAllText(Path.Combine(device, file)).Trim() : string.Empty;
}
