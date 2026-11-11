namespace BlueHeighliner.Bundler.Tests;

public sealed class NuGetBundleBuilderTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IResources> resources = new();
    private readonly Mock<IReleaseSources> sources = new();
    private readonly Mock<INuGetMirror> mirror = new();
    private readonly Mock<INuGetLicenses> licenses = new();
    private readonly Mock<IDotNetSdks> sdks = new();
    private readonly List<(string Channel, IReadOnlyList<NuGetPackage> Packages)> restored = [];
    private readonly List<(string Id, string Version)> fetched = [];
    private readonly NuGetBundleBuilder builder;

    public NuGetBundleBuilderTests()
    {
        sdks.Setup(s => s.Releases(It.IsAny<CancellationToken>())).ReturnsAsync(
            (IReadOnlyList<DotNetRelease>)[Release("10.0", "10.0.404", "10.0.15"), Release("11.0", "11.0.100", "11.0.0")]);
        sources.Setup(s => s.LatestNuGet(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, string? range, CancellationToken _) => (id, range) switch
        {
            ("Microsoft.Extensions.Hosting", "10.*") => "10.0.15",
            ("Microsoft.Extensions.Hosting", "11.*") => "11.0.0",
            ("xunit.v3", null) => "4.0.2",
            ("Old.Package", "10.*") => "10.2.0",
            ("Old.Package", "11.*") => null,
            _ => null,
        });
        mirror.Setup(m => m.Restore(It.IsAny<DotNetRelease>(), It.IsAny<IReadOnlyList<NuGetPackage>>(), It.IsAny<CancellationToken>())).Returns((DotNetRelease release, IReadOnlyList<NuGetPackage> packages, CancellationToken _) =>
        {
            restored.Add((release.Channel, packages));
            return Task.CompletedTask;
        });
        mirror.Setup(m => m.FetchExact(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns((string id, string version, CancellationToken _) =>
        {
            fetched.Add((id, version));
            return Task.CompletedTask;
        });
        licenses.Setup(l => l.Report(It.IsAny<string>())).Returns([]);
        licenses.Setup(l => l.Format(It.IsAny<IReadOnlyList<LicenseRow>>())).Returns(string.Empty);
        Directory.CreateDirectory(directory.Combine("bundle", "nuget"));
        builder = new(resources.Object, new NuGetPackageList(), sources.Object, mirror.Object, Mock.Of<IHttpFetcher>(), Mock.Of<ITemplatePatcher>(), licenses.Object, sdks.Object, Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    private static DotNetRelease Release(string channel, string version, string runtime)
        => new() { Channel = channel, Version = version, Runtime = runtime, Url = "https://example.com/" + version, Hash = new("sha512", "aa"), FileName = $"dotnet-sdk-{version}-linux-x64.tar.gz" };

    private Task<NuGetBuildResult> Build(string list)
    {
        resources.Setup(r => r.Text("list.txt")).Returns(list);
        return builder.Build(new() { PackageList = "list.txt" }, directory.Combine("bundle"));
    }

    [Fact]
    public async Task Build_RestoresOncePerReleaseAndTakesTheVersionThatGoesWithEachOne()
    {
        await Build("Microsoft.Extensions.Hosting@dotnet\nxunit.v3\n");

        Assert.Equal(["10.0", "11.0"], restored.Select(item => item.Channel));
        Assert.Equal("10.*", restored[0].Packages.Single(package => package.Id == "Microsoft.Extensions.Hosting").Version);
        Assert.Equal("11.*", restored[1].Packages.Single(package => package.Id == "Microsoft.Extensions.Hosting").Version);
        Assert.All(restored, item => Assert.Null(item.Packages.Single(package => package.Id == "xunit.v3").Version));
        Assert.Equal(
            [("Microsoft.Extensions.Hosting", "10.0.15"), ("xunit.v3", "4.0.2"), ("Microsoft.Extensions.Hosting", "11.0.0"), ("xunit.v3", "4.0.2")],
            fetched);
    }

    [Fact]
    public async Task Build_ExactPinnedVersion_IsUsedForEveryRelease()
    {
        await Build("Some.Package@4.1.0\n");

        Assert.Equal([("Some.Package", "4.1.0"), ("Some.Package", "4.1.0")], fetched);
        Assert.All(restored, item => Assert.Equal("4.1.0", Assert.Single(item.Packages).Version));
    }

    [Fact]
    public async Task Build_PackageWithNoVersionForOneRelease_IsLeftOutOfThatRestore()
    {
        await Build("Old.Package@dotnet\nxunit.v3\n");

        Assert.Contains(restored[0].Packages, package => package.Id == "Old.Package");
        Assert.DoesNotContain(restored[1].Packages, package => package.Id == "Old.Package");
        Assert.Contains(("Old.Package", "10.2.0"), fetched);
        Assert.DoesNotContain(fetched, item => item.Id == "Old.Package" && item.Version != "10.2.0");
    }

    [Fact]
    public async Task Build_PackageWithNoStableVersionAtAll_Throws()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Build("Missing.Package\n"));

        Assert.Contains("Missing.Package", error.Message);
    }

    [Fact]
    public async Task Build_OptionalPackageWithNoStableVersion_IsSkipped()
    {
        await Build("?Missing.Package\nxunit.v3\n");

        Assert.DoesNotContain(fetched, item => item.Id == "Missing.Package");
    }

    [Fact]
    public async Task Build_ListsEachVersionOfAPackageInItsComponent()
    {
        NuGetBuildResult result = await Build("Microsoft.Extensions.Hosting@dotnet\nxunit.v3\n");

        Assert.Contains("NuGet Microsoft.Extensions.Hosting 10.0.15, 11.0.0", result.Components);
        Assert.Contains("NuGet xunit.v3 4.0.2", result.Components);
    }
}
