namespace BlueHeighliner.Bundler.Tests;

public sealed class NuGetPackageListTests
{
    private readonly NuGetPackageList list = new();

    [Fact]
    public void Parse_ReadsAllLineForms()
    {
        IReadOnlyList<NuGetPackage> packages = list.Parse("# comment\nAvalonia\nMoq@4.21.0  # pinned\n?Optional.Package\n=dotnet-ef\n\n");

        Assert.Equal(["Avalonia", "Moq", "Optional.Package", "dotnet-ef"], packages.Select(package => package.Id));
        Assert.Null(packages[0].Version);
        Assert.Equal("4.21.0", packages[1].Version);
        Assert.True(packages[2].Optional);
        Assert.True(packages[3].Alone);
    }

    [Theory]
    [InlineData("Microsoft.NETCore.App.Runtime.linux-x64", true)]
    [InlineData("Microsoft.NETCore.App.Host.win-x64", true)]
    [InlineData("Microsoft.NETCore.App.Runtime.linux-musl-arm64", true)]
    [InlineData("Avalonia.Native", false)]
    [InlineData("Some.Package.x64", false)]
    public void IsPlatformPack_RecognizesRuntimePacks(string id, bool expected)
        => Assert.Equal(expected, list.IsPlatformPack(id));

    [Fact]
    public void Parse_VersionRange_IsKeptAsWritten()
    {
        IReadOnlyList<NuGetPackage> packages = new NuGetPackageList().Parse("Microsoft.Extensions.Hosting@10.*\n=Some.Tool@10.*\n");

        Assert.Equal("10.*", packages[0].Version);
        Assert.Equal("Some.Tool", packages[1].Id);
        Assert.Equal("10.*", packages[1].Version);
        Assert.True(packages[1].Alone);
    }
}
