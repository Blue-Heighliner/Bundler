namespace BlueHeighliner.Bundler.Tests;

public sealed class DebIndexTests
{
    private readonly Mock<IProcessRunner> runner = new();
    private readonly DebIndex index;

    public DebIndexTests()
        => index = new(runner.Object, new Hashing());

    [Fact]
    public void Field_ReadsOneControlField()
    {
        string control = "Package: git\nVersion: 1:2.56.0-1\nDescription: fast\n more";

        Assert.Equal("git", index.Field(control, "Package"));
        Assert.Equal("1:2.56.0-1", index.Field(control, "Version"));
        Assert.Equal(string.Empty, index.Field(control, "Missing"));
    }

    [Fact]
    public async Task Write_ThenRead_ListsThePackagesAndIsDeterministic()
    {
        using TempDirectory directory = new();
        directory.Write("b_1.0_amd64.deb", "bbb");
        directory.Write("a_2.0_amd64.deb", "aaa");
        runner.Setup(r => r.Run(It.Is<ProcessCommand>(c => c.FileName == "dpkg-deb" && c.Arguments[1].EndsWith("a_2.0_amd64.deb")), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, "Package: a\nVersion: 2.0\n", string.Empty));
        runner.Setup(r => r.Run(It.Is<ProcessCommand>(c => c.FileName == "dpkg-deb" && c.Arguments[1].EndsWith("b_1.0_amd64.deb")), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, "Package: b\nVersion: 1.0\n", string.Empty));

        IReadOnlyList<DebPackage> written = await index.Write(directory.Path);
        string firstRelease = File.ReadAllText(directory.Combine("Release"));
        await index.Write(directory.Path);

        Assert.Equal(["a", "b"], written.Select(package => package.Package));
        Assert.Equal(["a", "b"], index.Read(directory.Path).Select(package => package.Package));
        Assert.Equal("a_2.0_amd64.deb", index.Read(directory.Path)[0].FileName);
        Assert.Equal(firstRelease, File.ReadAllText(directory.Combine("Release")));
        Assert.DoesNotContain("Date:", firstRelease);
    }

    [Fact]
    public void Read_NoIndex_ReturnsNothing()
    {
        using TempDirectory directory = new();

        Assert.Empty(index.Read(directory.Path));
    }
}
