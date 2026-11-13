namespace BlueHeighliner.Bundler.Tests;

public sealed class ByteSizesTests
{
    private readonly ByteSizes sizes = new();

    [Theory]
    [InlineData("4700000000", 4_700_000_000)]
    [InlineData("4.7G", 4_700_000_000)]
    [InlineData("4.7GB", 4_700_000_000)]
    [InlineData("4.7 gb", 4_700_000_000)]
    [InlineData("700M", 700_000_000)]
    [InlineData("700MB", 700_000_000)]
    [InlineData("1.5K", 1_500)]
    [InlineData("25G", 25_000_000_000)]
    [InlineData("1T", 1_000_000_000_000)]
    [InlineData("4GiB", 4_294_967_296)]
    [InlineData("700MiB", 734_003_200)]
    [InlineData("1024B", 1_024)]
    [InlineData("  8M  ", 8_000_000)]
    public void Parse_ReadsASize(string text, long expected)
        => Assert.Equal(expected, sizes.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NothingTyped_MeansNoSize(string? text)
        => Assert.Null(sizes.Parse(text));

    [Theory]
    [InlineData("big")]
    [InlineData("G4")]
    [InlineData("4X")]
    [InlineData("-4G")]
    [InlineData("4..7G")]
    [InlineData("4 5 G")]
    public void Parse_NotASize_Throws(string text)
        => Assert.Throws<FormatException>(() => sizes.Parse(text));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1_500, "1.5 KB")]
    [InlineData(700_000_000, "700 MB")]
    [InlineData(4_700_372_992, "4.7 GB")]
    [InlineData(18_731_059_200, "18.73 GB")]
    [InlineData(2_000_000_000_000, "2 TB")]
    public void Format_UsesTheLargestDecimalUnit(long bytes, string expected)
        => Assert.Equal(expected, sizes.Format(bytes));
}
