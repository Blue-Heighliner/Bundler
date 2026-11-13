namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleFilesTests
{
    private static BundleFiles Create(TempDirectory directory)
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.Bundles).Returns(directory.Combine("bundles"));
        return new(paths.Object);
    }

    private static string Bytes(TempDirectory directory, string name, int length, byte fill = 1)
    {
        string path = directory.Combine("bundles", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, length).ToArray());
        return path;
    }

    [Fact]
    public void Find_ReturnsTheFileOfTheBundleOnly()
    {
        using TempDirectory directory = new();
        foreach (string name in new[] { "git.bundle", "git.1.bundle", "gitk.bundle", "git.bundle.partial", "gitk.bundle.001", "readme.txt" })
        {
            directory.Write("bundles/" + name, "x");
        }

        BundleFile found = Create(directory).Find("git")!;

        Assert.Equal([directory.Combine("bundles", "git.bundle")], found.Paths);
        Assert.False(found.IsSplit);
        Assert.Null(found.Problem);
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
    public void Find_Parts_AreOrderedByNumberAndAddedUp()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.002", 100);
        Bytes(directory, "big.bundle.001", 100);
        Bytes(directory, "big.bundle.003", 40);

        BundleFile found = Create(directory).Find("big")!;

        Assert.True(found.IsSplit);
        Assert.Equal(["big.bundle.001", "big.bundle.002", "big.bundle.003"], found.Paths.Select(Path.GetFileName));
        Assert.Equal(240, found.Length);
        Assert.Null(found.Problem);
    }

    [Fact]
    public void Find_Parts_NumbersBeyondNinetyNineNineAreOrderedAsNumbers()
    {
        using TempDirectory directory = new();
        foreach (int number in Enumerable.Range(1, 1002))
        {
            Bytes(directory, $"big.bundle.{number:D3}", number == 1002 ? 5 : 10);
        }

        BundleFile found = Create(directory).Find("big")!;

        Assert.Equal(1002, found.Paths.Count);
        Assert.EndsWith("big.bundle.1002", found.Paths[^1]);
        Assert.Null(found.Problem);
    }

    [Fact]
    public void Find_PartMissingInTheMiddle_IsAProblem()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.001", 100);
        Bytes(directory, "big.bundle.003", 40);

        Assert.Equal("part 2 is missing", Create(directory).Find("big")!.Problem);
    }

    [Fact]
    public void Find_FirstPartMissing_IsAProblem()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.002", 100);

        Assert.Equal("part 1 is missing", Create(directory).Find("big")!.Problem);
    }

    [Fact]
    public void Find_AMiddlePartThatIsShort_IsAProblem()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.001", 100);
        Bytes(directory, "big.bundle.002", 60);
        Bytes(directory, "big.bundle.003", 40);

        Assert.Contains("part 2", Create(directory).Find("big")!.Problem);
    }

    [Fact]
    public void Find_ALastPartBiggerThanTheFirst_IsAProblem()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.001", 100);
        Bytes(directory, "big.bundle.002", 120);

        Assert.NotNull(Create(directory).Find("big")!.Problem);
    }

    [Fact]
    public void Find_SingleFileAndPartsBoth_TheOneChangedLastWins()
    {
        using TempDirectory directory = new();
        string single = Bytes(directory, "big.bundle", 50);
        string part = Bytes(directory, "big.bundle.001", 10);
        File.SetLastWriteTime(single, new DateTime(2026, 1, 1));
        File.SetLastWriteTime(part, new DateTime(2026, 6, 1));

        Assert.True(Create(directory).Find("big")!.IsSplit);

        File.SetLastWriteTime(single, new DateTime(2026, 9, 1));

        Assert.False(Create(directory).Find("big")!.IsSplit);
    }

    [Fact]
    public void Find_ModifiedIsTheNewestPart()
    {
        using TempDirectory directory = new();
        File.SetLastWriteTime(Bytes(directory, "big.bundle.001", 10), new DateTime(2026, 1, 1));
        File.SetLastWriteTime(Bytes(directory, "big.bundle.002", 10), new DateTime(2026, 3, 3));

        Assert.Equal(new DateTime(2026, 3, 3), Create(directory).Find("big")!.Modified);
    }

    [Fact]
    public void PathFor_UsesTheName()
    {
        using TempDirectory directory = new();

        Assert.Equal(directory.Combine("bundles", "git.bundle"), Create(directory).PathFor("git"));
    }

    [Theory]
    [InlineData(1, "git.bundle.001")]
    [InlineData(12, "git.bundle.012")]
    [InlineData(1000, "git.bundle.1000")]
    public void PartPath_PadsTheNumberToThreeDigits(int number, string expected)
    {
        using TempDirectory directory = new();

        Assert.Equal(directory.Combine("bundles", expected), Create(directory).PartPath("git", number));
    }

    [Fact]
    public async Task Write_NoLimit_MakesOneFile()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);

        BundleFile written = await files.Write("git", null, async output => await output.WriteAsync(new byte[5000]));

        Assert.False(written.IsSplit);
        Assert.Equal(5000, written.Length);
        Assert.Equal(["git.bundle"], Directory.EnumerateFiles(directory.Combine("bundles")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Write_SmallerThanTheLimit_MakesOneFile()
    {
        using TempDirectory directory = new();

        BundleFile written = await Create(directory).Write("git", 10_000, async output => await output.WriteAsync(new byte[9_999]));

        Assert.False(written.IsSplit);
        Assert.Equal(["git.bundle"], Directory.EnumerateFiles(directory.Combine("bundles")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Write_ExactlyTheLimit_MakesOneFileAndNoEmptyPart()
    {
        using TempDirectory directory = new();

        BundleFile written = await Create(directory).Write("git", 10_000, async output => await output.WriteAsync(new byte[10_000]));

        Assert.False(written.IsSplit);
        Assert.Equal(10_000, new FileInfo(written.Paths[0]).Length);
    }

    [Fact]
    public async Task Write_BiggerThanTheLimit_MakesPartsOfExactlyTheLimitAndAShorterLastOne()
    {
        using TempDirectory directory = new();

        BundleFile written = await Create(directory).Write("git", 4_000, async output => await output.WriteAsync(new byte[10_500]));

        Assert.True(written.IsSplit);
        Assert.Equal([4_000, 4_000, 2_500], written.Paths.Select(path => new FileInfo(path).Length));
        Assert.Equal(["git.bundle.001", "git.bundle.002", "git.bundle.003"], written.Paths.Select(Path.GetFileName));
        Assert.Null(written.Problem);
        Assert.Empty(Directory.EnumerateFiles(directory.Combine("bundles"), "*.partial"));
    }

    [Fact]
    public async Task Write_ManySmallWritesAcrossThePartBoundary_KeepEveryByteInOrder()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);
        byte[] data = RandomNumberGenerator.GetBytes(9_001);

        BundleFile written = await files.Write("git", 1_000, async output =>
        {
            for (int offset = 0; offset < data.Length; offset += 777)
            {
                await output.WriteAsync(data.AsMemory(offset, Math.Min(777, data.Length - offset)));
            }
        });

        await using Stream input = files.OpenRead(written);
        using MemoryStream joined = new();
        await input.CopyToAsync(joined);
        Assert.Equal(data, joined.ToArray());
        Assert.Equal(10, written.Paths.Count);
    }

    [Fact]
    public async Task Write_ReplacesTheOlderBundleInEitherForm()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);
        await files.Write("git", 1_000, async output => await output.WriteAsync(new byte[3_500]));
        Assert.Equal(4, Directory.EnumerateFiles(directory.Combine("bundles")).Count());

        await files.Write("git", null, async output => await output.WriteAsync(new byte[500]));

        Assert.Equal(["git.bundle"], Directory.EnumerateFiles(directory.Combine("bundles")).Select(Path.GetFileName));

        await files.Write("git", 1_000, async output => await output.WriteAsync(new byte[2_500]));

        Assert.Equal(["git.bundle.001", "git.bundle.002", "git.bundle.003"], Directory.EnumerateFiles(directory.Combine("bundles")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Write_FewerPartsThanBefore_LeavesNoOldPartsBehind()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);
        await files.Write("git", 1_000, async output => await output.WriteAsync(new byte[5_000]));

        await files.Write("git", 1_000, async output => await output.WriteAsync(new byte[2_000]));

        Assert.Equal(2, Directory.EnumerateFiles(directory.Combine("bundles")).Count());
    }

    [Fact]
    public async Task Write_Fails_KeepsTheOlderBundleAndRemovesWhatWasStarted()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);
        await files.Write("git", null, async output => await output.WriteAsync(new byte[100]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => files.Write("git", 10, async output =>
        {
            await output.WriteAsync(new byte[55]);
            throw new InvalidOperationException("stop");
        }));

        Assert.Equal(["git.bundle"], Directory.EnumerateFiles(directory.Combine("bundles")).Select(Path.GetFileName));
        Assert.Equal(100, new FileInfo(directory.Combine("bundles", "git.bundle")).Length);
    }

    [Fact]
    public async Task Write_DoesNotTouchOtherBundlesWithTheSamePrefix()
    {
        using TempDirectory directory = new();
        Bytes(directory, "git-extra.bundle", 10);
        Bytes(directory, "git-extra.bundle.001", 10);
        BundleFiles files = Create(directory);

        await files.Write("git", 4, async output => await output.WriteAsync(new byte[10]));

        Assert.True(File.Exists(directory.Combine("bundles", "git-extra.bundle")));
        Assert.True(File.Exists(directory.Combine("bundles", "git-extra.bundle.001")));
    }

    [Fact]
    public async Task Write_RemovesAPartialLeftOverByAnEarlierFailedRun()
    {
        using TempDirectory directory = new();
        Bytes(directory, "git.bundle.007.partial", 10);

        await Create(directory).Write("git", null, async output => await output.WriteAsync(new byte[10]));

        Assert.False(File.Exists(directory.Combine("bundles", "git.bundle.007.partial")));
    }

    [Fact]
    public void OpenRead_AProblemBundle_Throws()
    {
        using TempDirectory directory = new();
        Bytes(directory, "big.bundle.002", 100);

        Assert.Throws<InvalidDataException>(() => Create(directory).OpenRead(Create(directory).Find("big")!));
    }

    [Fact]
    public async Task OpenRead_Parts_SeeksAcrossTheJoinedBytes()
    {
        using TempDirectory directory = new();
        BundleFiles files = Create(directory);
        byte[] data = Enumerable.Range(0, 2_500).Select(index => (byte)(index % 251)).ToArray();
        BundleFile written = await files.Write("git", 1_000, async output => await output.WriteAsync(data));

        await using Stream input = files.OpenRead(written);
        Assert.Equal(2_500, input.Length);
        input.Seek(950, SeekOrigin.Begin);
        byte[] across = new byte[100];
        await input.ReadExactlyAsync(across);
        Assert.Equal(data[950..1050], across);
        input.Seek(-10, SeekOrigin.End);
        byte[] tail = new byte[20];
        Assert.Equal(10, await input.ReadAsync(tail));
        Assert.Equal(data[^10..], tail[..10]);
        Assert.Equal(0, await input.ReadAsync(tail));
    }

    [Theory]
    [InlineData(null, false, new[] { 100L }, true)]
    [InlineData(null, true, new[] { 100L, 100L, 50L }, false)]
    [InlineData(200L, false, new[] { 100L }, true)]
    [InlineData(100L, false, new[] { 100L }, true)]
    [InlineData(50L, false, new[] { 100L }, false)]
    [InlineData(100L, true, new[] { 100L, 100L, 30L }, true)]
    [InlineData(80L, true, new[] { 100L, 100L, 30L }, false)]
    [InlineData(100L, true, new[] { 100L, 60L, 60L }, false)]
    [InlineData(200L, true, new[] { 100L, 100L, 30L }, false)]
    public void Matches_TellsWhetherTheFilesAreLaidOutForTheLimit(long? limit, bool split, long[] sizes, bool expected)
    {
        using TempDirectory directory = new();
        string[] paths = [.. sizes.Select((size, index) => Bytes(directory, $"x.bundle.{index + 1:D3}", (int)size))];

        Assert.Equal(expected, new BundleFile { Name = "x", Paths = paths, Length = sizes.Sum(), Modified = DateTime.Now, IsSplit = split }.Matches(limit));
    }
}
