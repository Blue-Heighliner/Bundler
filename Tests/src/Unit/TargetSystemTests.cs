namespace BlueHeighliner.Bundler.Tests;

public sealed class TargetSystemTests
{
    private readonly Mock<IResources> resources = new();

    private TargetSystem Target(string status)
    {
        resources.Setup(r => r.Text("target/dpkg-status.txt")).Returns(status);
        return new(resources.Object);
    }

    [Fact]
    public void Kernel_IsTheInstalledGenericKernelImage()
    {
        TargetSystem target = Target("Package: linux-image-generic-hwe-24.04\nVersion: 7.0.0-31.31~24.04.1\n\nPackage: linux-image-7.0.0-31-generic\nVersion: 7.0.0-31.31~24.04.1\n");

        Assert.Equal("7.0.0-31-generic", target.Kernel);
    }

    [Fact]
    public void Kernel_NoKernelImage_Throws()
        => Assert.Throws<InvalidOperationException>(() => Target("Package: git\nVersion: 1\n").Kernel);

    [Fact]
    public void IsInstalled_MatchesWholePackageNamesOnly()
    {
        TargetSystem target = Target("Package: zlib1g\nVersion: 1\n\nPackage: libgimp2.0t64\nVersion: 2\n");

        Assert.True(target.IsInstalled("zlib1g"));
        Assert.True(target.IsInstalled("libgimp2.0t64"));
        Assert.False(target.IsInstalled("zlib"));
        Assert.False(target.IsInstalled("gimp"));
    }

    [Fact]
    public void PackageStatus_IsReadOnce()
    {
        TargetSystem target = Target("Package: git\nVersion: 1\n");

        _ = target.PackageStatus;
        _ = target.PackageStatus;
        _ = target.IsInstalled("git");

        resources.Verify(r => r.Text("target/dpkg-status.txt"), Times.Once);
    }

    [Fact]
    public void BuiltInPackageList_NamesTheKernelTheUsbInstalls()
    {
        TargetSystem target = new(new Resources());

        Assert.Equal("7.0.0-31-generic", target.Kernel);
        Assert.True(target.IsInstalled("libc6"));
        Assert.True(target.IsInstalled("ubuntu-desktop"));
    }
}
