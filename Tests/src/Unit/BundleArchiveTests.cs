using System.Formats.Tar;

namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleArchiveTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly BundleFiles files;
    private readonly BundleArchive archive;

    public BundleArchiveTests()
    {
        Mock<IPaths> paths = new();
        paths.SetupGet(p => p.Bundles).Returns(directory.Combine("bundles"));
        files = new(paths.Object);
        archive = new(new Hashing(), files);
    }

    public void Dispose()
        => directory.Dispose();

    private static BundleInfo Info()
        => new() { Name = "demo", Components = ["demo 1.0"] };

    private async Task<BundleFile> Pack(long? limit = null)
    {
        string payload = directory.Combine("payload");
        IReadOnlyDictionary<string, string> sums = await archive.ComputeChecksums(payload);
        return await files.Write("demo", limit, output => archive.Pack(payload, output, Info(), sums));
    }

    [Fact]
    public async Task ComputeChecksums_SkipsTheBundleMetadataFiles()
    {
        directory.Write("a.txt", "a");
        directory.Write("sub/b.txt", "b");
        directory.Write("bundle.info", "x");
        directory.Write("SHA256SUMS", "x");

        IReadOnlyDictionary<string, string> sums = await archive.ComputeChecksums(directory.Path);

        Assert.Equal(["a.txt", "sub/b.txt"], sums.Keys.Where(key => key is "a.txt" or "sub/b.txt"));
        Assert.DoesNotContain("bundle.info", sums.Keys);
        Assert.DoesNotContain("SHA256SUMS", sums.Keys);
    }

    [Fact]
    public async Task Pack_ThenReadInfoAndChecksums_ReturnsWhatWasPacked()
    {
        directory.Write("payload/a.txt", "alpha");

        BundleFile file = await Pack();

        Assert.Equal("demo", archive.ReadInfo(file)!.Name);
        Assert.Equal(await archive.ComputeChecksums(directory.Combine("payload")), archive.ReadChecksums(file));
        Assert.False(file.IsSplit);
        Assert.Empty(Directory.EnumerateFiles(directory.Combine("bundles"), "*.partial"));
    }

    [Fact]
    public async Task Extract_ThenVerify_FindsNothingDamaged()
    {
        directory.Write("payload/a.txt", "alpha");
        directory.Write("payload/deep/b.txt", "beta");
        BundleFile file = await Pack();

        await archive.Extract(file, directory.Combine("out"));

        Assert.Equal("beta", File.ReadAllText(directory.Combine("out", "deep", "b.txt")));
        Assert.Empty(await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Extract_FromParts_GivesTheSameFilesAsFromOneFile()
    {
        File.WriteAllBytes(directory.Write("payload/small.txt", "alpha") + ".bin", RandomNumberGenerator.GetBytes(300_000));
        File.WriteAllBytes(directory.Combine("payload", "big.bin"), RandomNumberGenerator.GetBytes(900_000));
        BundleFile single = await Pack();
        await archive.Extract(single, directory.Combine("from-single"));

        BundleFile parts = await Pack(limit: 256 * 1024);
        await archive.Extract(parts, directory.Combine("from-parts"));

        Assert.True(parts.IsSplit);
        Assert.True(parts.Paths.Count > 4);
        Assert.Equal(await archive.ComputeChecksums(directory.Combine("from-single")), await archive.ComputeChecksums(directory.Combine("from-parts")));
        Assert.Empty(await archive.Verify(directory.Combine("from-parts")));
    }

    [Fact]
    public async Task ReadInfoAndChecksums_FromParts_ReturnWhatWasPacked()
    {
        File.WriteAllBytes(directory.Write("payload/a.txt", "alpha") + ".bin", RandomNumberGenerator.GetBytes(600_000));

        BundleFile parts = await Pack(limit: 200_000);

        Assert.True(parts.Paths.Count > 2);
        Assert.Equal("demo", archive.ReadInfo(parts)!.Name);
        Assert.Equal(await archive.ComputeChecksums(directory.Combine("payload")), archive.ReadChecksums(parts));
    }

    [Fact]
    public async Task Extract_PartsWithOneMissing_Throws()
    {
        File.WriteAllBytes(directory.Write("payload/a.txt", "alpha") + ".bin", RandomNumberGenerator.GetBytes(600_000));
        BundleFile parts = await Pack(limit: 200_000);
        File.Delete(parts.Paths[1]);

        BundleFile found = files.Find("demo")!;

        Assert.NotNull(found.Problem);
        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(found, directory.Combine("out")));
    }

    [Fact]
    public async Task Extract_PartsWithTheLastOneMissing_ThrowsInvalidData()
    {
        File.WriteAllBytes(directory.Write("payload/a.txt", "alpha") + ".bin", RandomNumberGenerator.GetBytes(600_000));
        BundleFile parts = await Pack(limit: 200_000);
        File.Delete(parts.Paths[^1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(files.Find("demo")!, directory.Combine("out")));
    }

    [Fact]
    public async Task Verify_ChangedFile_IsReported()
    {
        directory.Write("payload/a.txt", "alpha");
        await archive.Extract(await Pack(), directory.Combine("out"));
        File.WriteAllText(directory.Combine("out", "a.txt"), "tampered");

        Assert.Equal(["a.txt"], await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Verify_MissingFile_IsReported()
    {
        directory.Write("payload/a.txt", "alpha");
        await archive.Extract(await Pack(), directory.Combine("out"));
        File.Delete(directory.Combine("out", "a.txt"));

        Assert.Single(await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Extract_EntryOutsideTheFolder_Throws()
    {
        Directory.CreateDirectory(directory.Combine("bundles"));
        await using (FileStream stream = File.Create(directory.Combine("bundles", "evil.bundle")))
        await using (TarWriter writer = new(stream, TarEntryFormat.Pax))
        {
            PaxTarEntry entry = new(TarEntryType.RegularFile, "../escaped.txt") { DataStream = new MemoryStream("x"u8.ToArray()) };
            await writer.WriteEntryAsync(entry);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(files.Find("evil")!, directory.Combine("out")));
        Assert.False(File.Exists(directory.Combine("escaped.txt")));
    }

    [Fact]
    public async Task Extract_TruncatedFile_ThrowsInvalidData()
    {
        Directory.CreateDirectory(directory.Combine("payload"));
        File.WriteAllBytes(directory.Combine("payload", "big.bin"), new byte[200_000]);
        BundleFile file = await Pack();
        using (FileStream stream = new(file.Paths[0], FileMode.Open))
        {
            stream.SetLength(stream.Length / 2);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(files.Find("demo")!, directory.Combine("out")));
    }

    [Fact]
    public void ReadInfo_NotABundle_ReturnsNull()
    {
        directory.Write("bundles/junk.bundle", "not a tar file");

        Assert.Null(archive.ReadInfo(files.Find("junk")!));
    }

    [Fact]
    public async Task Pack_SameContent_GivesSameChecksums()
    {
        directory.Write("one/a.txt", "same");
        directory.Write("two/a.txt", "same");

        Assert.Equal(await archive.ComputeChecksums(directory.Combine("one")), await archive.ComputeChecksums(directory.Combine("two")));
    }
}
