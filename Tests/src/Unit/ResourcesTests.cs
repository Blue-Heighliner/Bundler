namespace BlueHeighliner.Bundler.Tests;

public sealed class ResourcesTests
{
    private readonly Resources resources = new();

    [Fact]
    public void Bytes_ReadsTheAutoRemoteIconAsAPng()
    {
        ReadOnlyMemory<byte> icon = resources.Bytes("auto-remote/icon.png");

        Assert.True(icon.Length > 10_000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, icon.Span[..8].ToArray());
    }

    [Fact]
    public void Bytes_UnknownFile_Throws()
        => Assert.Throws<FileNotFoundException>(() => resources.Bytes("nothing/here.bin"));

    [Fact]
    public void Text_StillReadsTextFiles()
        => Assert.Contains("##", resources.Text("Bundles.md"));
}
