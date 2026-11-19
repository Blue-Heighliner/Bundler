namespace BlueHeighliner.Bundler.Tests;

public sealed class UninstallScriptAptTests : IDisposable
{
    private readonly UninstallHarness harness = new();

    private string Leftover => Path.Combine(harness.Root, "state", "apt-leftover");

    public void Dispose()
        => harness.Dispose();

    private BundleState State(string bundle, params string[] added)
    {
        BundleState state = new(Path.Combine(harness.Root, "state", bundle));
        foreach (string package in added)
        {
            state.Add("apt-new", package);
        }

        return state;
    }

    private async Task Remove(BundleState state)
    {
        UninstallScript script = new();
        script.Add("apt-remove-added");
        await harness.Run(state, script);
    }

    [Fact]
    public async Task RemovesWhatNothingElseNeeds()
    {
        harness.Installed("liba", "libb");
        harness.Removable("liba", "libb");

        await Remove(State("blender", "liba", "libb"));

        Assert.Empty(harness.InstalledNow);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task KeepsWhatAnotherBundleStillNeedsAndRemembersIt()
    {
        harness.Installed("liba", "libb", "another-bundles-package");
        harness.Removable("liba");
        harness.NeededBy("libb", "another-bundles-package");

        await Remove(State("blender", "liba", "libb"));

        Assert.Equal(["libb", "another-bundles-package"], harness.InstalledNow);
        Assert.Equal(["libb"], File.ReadAllLines(Leftover));
    }

    [Fact]
    public async Task APackageAptKeepsAsOneAlternativeOfADependencyButNothingNeeds_IsRemoved()
    {
        harness.Installed("openbox", "libobt2v5:amd64");

        await Remove(State("virtual-testing", "openbox", "libobt2v5:amd64"));

        Assert.Empty(harness.InstalledNow);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task APackageWhoseRemovalWouldTakeOtherSoftwareWithIt_IsKept()
    {
        harness.Installed("libneeded", "some-other-program");
        harness.NeededBy("libneeded", "some-other-program");

        await Remove(State("bundle", "libneeded"));

        Assert.Equal(["libneeded", "some-other-program"], harness.InstalledNow);
        Assert.Equal(["libneeded"], File.ReadAllLines(Leftover));
    }

    [Fact]
    public async Task ALaterUninstall_RemovesTheLeftoversOfAnEarlierOne()
    {
        harness.Installed("libb", "gimp");
        BundleState first = State("blender", "libb");
        BundleState second = State("gimp", "gimp");
        harness.NeededBy("libb", "gimp");

        await Remove(first);
        Assert.Equal(["libb"], File.ReadAllLines(Leftover));

        harness.Removable("libb", "gimp");
        File.Delete(Path.Combine(harness.World, "needed", "libb"));
        await Remove(second);

        Assert.Empty(harness.InstalledNow);
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task NothingRecorded_DoesNothing()
    {
        await Remove(State("git"));

        Assert.DoesNotContain(harness.Calls, call => call.StartsWith("apt-mark", StringComparison.Ordinal));
        Assert.False(File.Exists(Leftover));
    }

    [Fact]
    public async Task KernelPackagesGoExceptTheRunningOne()
    {
        string running = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        harness.Installed("linux-modules-7.9.9-1-generic", $"linux-modules-{running}");

        await Remove(State("a2000", "linux-modules-7.9.9-1-generic", $"linux-modules-{running}"));

        Assert.Equal([$"linux-modules-{running}"], harness.InstalledNow);
    }
}
