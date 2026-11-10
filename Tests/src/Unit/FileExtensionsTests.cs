namespace BlueHeighliner.Bundler.Tests;

public sealed class FileExtensionsTests : IDisposable
{
    private readonly TempDirectory directory = new();

    public void Dispose()
        => directory.Dispose();

    [Fact]
    public void DeleteIfPresent_DeletesAFile()
    {
        string path = directory.Write("a/file.txt", "x");

        File.DeleteIfPresent(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteIfPresent_MissingFile_DoesNothing()
    {
        directory.Write("a/other.txt", "x");

        File.DeleteIfPresent(directory.Combine("a", "missing.txt"));

        Assert.True(File.Exists(directory.Combine("a", "other.txt")));
    }

    [Fact]
    public void DeleteIfPresent_MissingFolder_DoesNotThrow()
        => File.DeleteIfPresent(directory.Combine("no", "such", "folder", "file.txt"));

    [Fact]
    public void DeleteIfPresent_DeletesALinkWithoutTouchingItsTarget()
    {
        string target = directory.Write("a/target.txt", "x");
        string link = directory.Combine("a", "link");
        File.CreateSymbolicLink(link, target);

        File.DeleteIfPresent(link);

        Assert.True(File.Exists(target));
        Assert.Null(new FileInfo(link).LinkTarget);
    }
}
