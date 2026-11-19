namespace BlueHeighliner.Bundler;

/// <summary>
/// The NVIDIA Linux driver for the RTX A2000 (Ampere), as the packages Ubuntu builds from it. The open kernel modules are prebuilt and
/// signed by Canonical, so they load with Secure Boot on and need no compiler, but each build is for one kernel. The bundle brings the modules
/// for the kernel of the target system (<see cref="ITargetSystem" />) and no kernel of its own, and uses the newest driver series whose
/// userspace packages and modules for that kernel fit together: Ubuntu stops building modules for an older kernel, so a newer series can have none.
/// </summary>
internal sealed partial class A2000Bundle(IReleaseSources sources, IAptBuilder apt, IAptInstaller installer, IDecisions decisions, IProcessRunner runner, IVersions versions, ITargetSystem target, ILog log) : BundleBase
{
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
        (string series, string driver, string modules) = await Series(int.Parse(latest.Split('.')[0]), cancellation);
        log.Info($"the newest series with prebuilt modules for kernel {target.Kernel} is {series}");
        IReadOnlyList<DebPackage> packages = await apt.Download(Path.Combine(directory, "debs"), new() { Services = [driver, modules] }, cancellation);
        string version = versions.Upstream(packages.First(package => package.Package == driver).Version, '-');
        if (version != latest)
        {
            log.Warn($"Ubuntu packages the NVIDIA {series} driver as {version}, NVIDIA's own latest is {latest}. The Ubuntu build is used.");
        }

        DebPackage modulesPackage = packages.First(package => package.Package == modules);
        List<string> components = [$"NVIDIA driver {version} ({driver})", $"{modulesPackage.Package} {modulesPackage.Version} (prebuilt for kernel {target.Kernel})"];
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
            string running = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
            string built = ModulesKernel(context);
            log.Warn(running == built
                ? "restart the computer to load the driver."
                : $"restart the computer to load the driver. The prebuilt modules are for kernel {built} but this computer runs {running}: the driver only loads with {built}, so start that kernel (pick it in the boot menu) or use a bundle built for {running}.");
        }

        ProcessResult cards = await runner.Try("nvidia-smi", ["-L"]);
        if (cards.Success && cards.Output.Contains("GPU"))
        {
            log.Info(cards.Text);
        }
    }

    /// <inheritdoc />
    public override UninstallScript Script(UninstallContext context)
    {
        UninstallScript script = new();
        if (context.State.IsOwned("driver"))
        {
            script.Add("apt-remove-added");
            script.Warn("restart the computer to unload the driver (the display falls back to the open nouveau driver).");
        }
        else
        {
            script.Info("the NVIDIA driver was already on this computer and was not installed by this bundle, so it is left in place");
            script.Add("apt-remove-added");
        }

        return script;
    }

    private string ModulesKernel(InstallContext context)
        => context.Info.Components.Select(component => KernelPattern().Match(component)).FirstOrDefault(match => match.Success)?.Groups["kernel"].Value ?? "of the bundle";

    private async Task<(string Series, string Driver, string Modules)> Series(int latest, CancellationToken cancellation)
    {
        IReadOnlyList<string> offered = await apt.FindAll(@"nvidia-driver-[0-9]+-open", cancellation);
        foreach (int series in offered.Select(name => int.Parse(name["nvidia-driver-".Length..^"-open".Length])).Where(series => series <= latest).OrderDescending())
        {
            string driver = $"nvidia-driver-{series}-open";
            string modules = $"linux-modules-nvidia-{series}-open-{target.Kernel}";
            if (await apt.Exists(modules, cancellation) && await apt.Plan([driver, modules], cancellation) is { } plan && plan.All(name => SeriesPattern().Match(name) is not { Success: true } match || int.Parse(match.Groups["series"].Value) == series))
            {
                return (series.ToString(), driver, modules);
            }

            log.Info($"the {series} series has no prebuilt modules for kernel {target.Kernel} that fit its driver packages");
        }

        throw new InvalidOperationException($"Ubuntu has no packaged NVIDIA open driver up to the {latest} series with prebuilt modules for kernel {target.Kernel}");
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

    [GeneratedRegex(@"prebuilt for kernel (?<kernel>\S+)\)")]
    private static partial Regex KernelPattern();

    [GeneratedRegex(@"nvidia[a-z-]*-(?<series>[0-9]{3})(?:-|$)")]
    private static partial Regex SeriesPattern();
}
