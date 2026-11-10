namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleFilesTests
{
    private static BundleFiles Create(TempDirectory directory)
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.Bundles).Returns(directory.Combine("bundles"));
        return new(paths.Object);
    }

    [Fact]
    public void Find_ReturnsTheFileOfTheBundleOnly()
    {
        using TempDirectory directory = new();
        foreach (string name in new[] { "git.bundle", "git.1.bundle", "gitk.bundle", "git.bundle.partial", "readme.txt" })
        {
            directory.Write("bundles/" + name, "x");
        }

        Assert.Equal(directory.Combine("bundles", "git.bundle"), Create(directory).Find("git"));
    }

    [Fact]
    public void Find_WorksForNamesThatStartWithADigit()
    {
        using TempDirectory directory = new();
        directory.Write("bundles/gimp.bundle", "x");

        Assert.NotNull(Create(directory).Find("gimp"));
        Assert.Null(Create(directory).Find("blender"));
    }

    [Fact]
    public void Find_NoFolderOrNoFile_ReturnsNull()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);

        Assert.Null(files.Find("git"));
        Directory.CreateDirectory(directory.Combine("bundles"));
        Assert.Null(files.Find("git"));
    }

    [Fact]
    public void PathFor_UsesTheName()
    {
        using TempDirectory directory = new();

        Assert.Equal(directory.Combine("bundles", "git.bundle"), Create(directory).PathFor("git"));
    }
}
