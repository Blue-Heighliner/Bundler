namespace BlueHeighliner.Bundler.Tests;

public sealed class PathsTests
{
    [Fact]
    public void Cache_AndState_LiveUnderTheDataFolder()
    {
        Mock<IMachine> machine = new();
        machine.SetupGet(m => m.DataDirectory).Returns("/root/.config/BlueHeighliner.Bundler");
        machine.SetupGet(m => m.ApplicationDirectory).Returns("/opt/bundler");

        Paths paths = new(machine.Object);

        Assert.Equal("/root/.config/BlueHeighliner.Bundler/state", paths.State);
        Assert.Equal("/root/.config/BlueHeighliner.Bundler/cache/downloads", paths.Downloads);
        Assert.Equal("/root/.config/BlueHeighliner.Bundler/cache/build", paths.Staging);
    }

    [Fact]
    public void Bundles_DefaultsToAFolderNextToTheExecutable()
    {
        Mock<IMachine> machine = new();
        machine.SetupGet(m => m.ApplicationDirectory).Returns("/opt/bundler");

        Assert.Equal("/opt/bundler/Bundles", new Paths(machine.Object).Bundles);
    }

    [Fact]
    public void Bundles_CanBeOverridden()
    {
        Mock<IMachine> machine = new();
        machine.SetupGet(m => m.ApplicationDirectory).Returns("/opt/bundler");

        Assert.Equal("/mnt/usb/bundles", new Paths(machine.Object, "/mnt/usb/bundles").Bundles);
    }

    [Fact]
    public void Bundles_OverrideIsMadeAbsolute()
        => Assert.Equal(Path.GetFullPath("relative/bundles"), new Paths(Mock.Of<IMachine>(), "relative/bundles").Bundles);

    [Fact]
    public void Machine_DataFolder_IsRootsAppDataFolderOnLinux()
    {
        Assert.True(OperatingSystem.IsLinux());

        Assert.EndsWith("/.config/BlueHeighliner.Bundler", new Machine().DataDirectory);
        Assert.DoesNotContain(Environment.UserName == "root" ? "never" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), new Machine().DataDirectory);
    }
}
