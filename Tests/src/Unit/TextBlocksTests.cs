namespace BlueHeighliner.Bundler.Tests;

public sealed class TextBlocksTests
{
    private readonly TextBlocks blocks = new();

    [Fact]
    public void Add_KeepsExistingLinesAndAppendsABlock()
    {
        using TempDirectory directory = new();
        string path = directory.Write("rc", "keep\n");

        blocks.Add(path, "tag", ["a", "b"]);

        Assert.Equal(["keep", "# bundler tag begin", "a", "b", "# bundler tag end"], File.ReadAllLines(path));
    }

    [Fact]
    public void Add_Twice_ReplacesTheBlock()
    {
        using TempDirectory directory = new();
        string path = directory.Combine("rc");

        blocks.Add(path, "tag", ["old"]);
        blocks.Add(path, "tag", ["new"]);

        Assert.Equal(["# bundler tag begin", "new", "# bundler tag end"], File.ReadAllLines(path));
    }

    [Fact]
    public void Remove_OnlyRemovesTheTaggedBlock()
    {
        using TempDirectory directory = new();
        string path = directory.Combine("rc");
        blocks.Add(path, "one", ["1"]);
        blocks.Add(path, "two", ["2"]);

        blocks.Remove(path, "one");

        Assert.Equal(["# bundler two begin", "2", "# bundler two end"], File.ReadAllLines(path));
    }

    [Fact]
    public void Remove_MissingFile_DoesNothing()
    {
        using TempDirectory directory = new();

        blocks.Remove(directory.Combine("none"), "tag");

        Assert.False(File.Exists(directory.Combine("none")));
    }
}
