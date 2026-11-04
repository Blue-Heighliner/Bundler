using System.Formats.Tar;

namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleArchiveTests
{
    private readonly BundleArchive archive = new(new Hashing());

    private static BundleInfo Info()
        => new() { Name = "demo", Components = ["demo 1.0"] };

    [Fact]
    public async Task ComputeChecksums_SkipsTheBundleMetadataFiles()
    {
        using TempDirectory directory = new();
        directory.Write("a.txt", "a");
        directory.Write("sub/b.txt", "b");
        directory.Write("bundle.info", "x");
        directory.Write("SHA256SUMS", "x");

        IReadOnlyDictionary<string, string> sums = await archive.ComputeChecksums(directory.Path);

        Assert.Equal(["a.txt", "sub/b.txt"], sums.Keys);
    }

    [Fact]
    public async Task Pack_ThenReadInfoAndChecksums_ReturnsWhatWasPacked()
    {
        using TempDirectory directory = new();
        string payload = directory.Combine("payload");
        File.WriteAllText(directory.Write("payload/a.txt", "alpha"), "alpha");
        string file = directory.Combine("out", "demo.1.bundle");
        IReadOnlyDictionary<string, string> sums = await archive.ComputeChecksums(payload);

        await archive.Pack(payload, file, Info(), sums);

        Assert.Equal("demo", archive.ReadInfo(file)!.Name);
        Assert.Equal(sums, archive.ReadChecksums(file));
        Assert.False(File.Exists(file + ".partial"));
    }

    [Fact]
    public async Task Extract_ThenVerify_FindsNothingDamaged()
    {
        using TempDirectory directory = new();
        string payload = directory.Combine("payload");
        directory.Write("payload/a.txt", "alpha");
        directory.Write("payload/deep/b.txt", "beta");
        string file = directory.Combine("demo.1.bundle");
        await archive.Pack(payload, file, Info(), await archive.ComputeChecksums(payload));

        await archive.Extract(file, directory.Combine("out"));

        Assert.Equal("beta", File.ReadAllText(directory.Combine("out", "deep", "b.txt")));
        Assert.Empty(await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Verify_ChangedFile_IsReported()
    {
        using TempDirectory directory = new();
        string payload = directory.Combine("payload");
        directory.Write("payload/a.txt", "alpha");
        string file = directory.Combine("demo.1.bundle");
        await archive.Pack(payload, file, Info(), await archive.ComputeChecksums(payload));
        await archive.Extract(file, directory.Combine("out"));
        File.WriteAllText(directory.Combine("out", "a.txt"), "tampered");

        Assert.Equal(["a.txt"], await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Verify_MissingFile_IsReported()
    {
        using TempDirectory directory = new();
        string payload = directory.Combine("payload");
        directory.Write("payload/a.txt", "alpha");
        string file = directory.Combine("demo.1.bundle");
        await archive.Pack(payload, file, Info(), await archive.ComputeChecksums(payload));
        await archive.Extract(file, directory.Combine("out"));
        File.Delete(directory.Combine("out", "a.txt"));

        Assert.Single(await archive.Verify(directory.Combine("out")));
    }

    [Fact]
    public async Task Extract_EntryOutsideTheFolder_Throws()
    {
        using TempDirectory directory = new();
        string file = directory.Combine("evil.bundle");
        await using (FileStream stream = File.Create(file))
        await using (TarWriter writer = new(stream, TarEntryFormat.Pax))
        {
            PaxTarEntry entry = new(TarEntryType.RegularFile, "../escaped.txt") { DataStream = new MemoryStream("x"u8.ToArray()) };
            await writer.WriteEntryAsync(entry);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(file, directory.Combine("out")));
        Assert.False(File.Exists(directory.Combine("escaped.txt")));
    }

    [Fact]
    public async Task Extract_TruncatedFile_ThrowsInvalidData()
    {
        using TempDirectory directory = new();
        string payload = directory.Combine("payload");
        File.WriteAllBytes(directory.Combine("payload.bin"), new byte[200_000]);
        Directory.CreateDirectory(payload);
        File.WriteAllBytes(Path.Combine(payload, "big.bin"), new byte[200_000]);
        string file = directory.Combine("demo.1.bundle");
        await archive.Pack(payload, file, Info(), await archive.ComputeChecksums(payload));
        using (FileStream stream = new(file, FileMode.Open))
        {
            stream.SetLength(stream.Length / 2);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => archive.Extract(file, directory.Combine("out")));
    }

    [Fact]
    public void ReadInfo_NotABundle_ReturnsNull()
    {
        using TempDirectory directory = new();

        Assert.Null(archive.ReadInfo(directory.Write("junk.bundle", "not a tar file")));
    }

    [Fact]
    public async Task Pack_SameContent_GivesSameChecksums()
    {
        using TempDirectory directory = new();
        directory.Write("one/a.txt", "same");
        directory.Write("two/a.txt", "same");

        Assert.Equal(await archive.ComputeChecksums(directory.Combine("one")), await archive.ComputeChecksums(directory.Combine("two")));
    }
}
