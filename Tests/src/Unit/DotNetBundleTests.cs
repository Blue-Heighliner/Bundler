namespace BlueHeighliner.Bundler.Tests;

public sealed class DotNetBundleTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IDotNetSdks> sdks = new();
    private readonly Mock<IDotNetFiles> files = new();
    private readonly Mock<INuGetFeed> feed = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly DotNetBundle bundle;
    private readonly BundleState state;

    public DotNetBundleTests()
    {
        files.SetupGet(f => f.Root).Returns(directory.Combine("usr", "share", "dotnet"));
        files.SetupGet(f => f.Link).Returns(directory.Combine("usr", "local", "bin", "dotnet"));
        files.SetupGet(f => f.NuGetConfig).Returns(directory.Combine("usr", "local", "share", "NuGet", "Config", "bundler.config"));
        feed.SetupGet(f => f.Directory).Returns(directory.Combine("opt", "bundler", "nuget-feed"));
        sdks.Setup(s => s.InstalledVersions(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(0, "sdks", string.Empty));
        state = new(directory.Combine("state"));
        directory.Write("payload/nuget/readme", "feed");
        Tarball("10.0.404", "10.0.15", "muxer10");
        Tarball("11.0.100", "11.0.0", "muxer11");
        bundle = new(sdks.Object, Mock.Of<IDownloads>(), Mock.Of<IAptBuilder>(), Mock.Of<IAptInstaller>(), Mock.Of<INuGetBundleBuilder>(), feed.Object, new Decisions(new Versions(), log.Object), new Versions(), files.Object, Mock.Of<IUsers>(), Mock.Of<ITextBlocks>(), runner.Object, log.Object);
    }

    public void Dispose()
        => directory.Dispose();

    private void Tarball(string sdk, string runtime, string muxer)
    {
        string source = directory.Combine("source", sdk);
        directory.Write($"source/{sdk}/dotnet", muxer);
        directory.Write($"source/{sdk}/LICENSE.txt", "license " + sdk);
        directory.Write($"source/{sdk}/host/fxr/{runtime}/libhostfxr.so", "fxr " + runtime);
        directory.Write($"source/{sdk}/sdk/{sdk}/sdk.dll", "sdk " + sdk);
        directory.Write($"source/{sdk}/shared/Microsoft.NETCore.App/{runtime}/runtime.dll", "runtime " + runtime);
        Directory.CreateDirectory(directory.Combine("payload", "dotnet"));
        using FileStream file = File.Create(directory.Combine("payload", "dotnet", $"dotnet-sdk-{sdk}-linux-x64.tar.gz"));
        using GZipStream gzip = new(file, CompressionLevel.Fastest);
        TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
    }

    private InstallContext Install()
        => new() { Payload = directory.Combine("payload"), State = state, Users = [], Info = new() { Name = "dotnet" } };

    private string Root(params string[] parts)
        => Path.Combine([files.Object.Root, .. parts]);

    [Fact]
    public void Describes_ItselfAsTheTwoSdksAndTheFeed()
    {
        Assert.Equal("dotnet", bundle.Name);
        Assert.Empty(bundle.Requires);
        Assert.Contains(".NET 11", bundle.Description);
    }

    [Fact]
    public async Task Install_IntoAnEmptyComputer_PutsBothSdksSideBySideWithTheNewerHost()
    {
        await bundle.Install(Install());

        Assert.True(File.Exists(Root("sdk", "10.0.404", "sdk.dll")));
        Assert.True(File.Exists(Root("sdk", "11.0.100", "sdk.dll")));
        Assert.True(File.Exists(Root("shared", "Microsoft.NETCore.App", "10.0.15", "runtime.dll")));
        Assert.True(File.Exists(Root("shared", "Microsoft.NETCore.App", "11.0.0", "runtime.dll")));
        Assert.Equal("muxer11", File.ReadAllText(Root("dotnet")));
        Assert.Equal(Root("dotnet"), new FileInfo(files.Object.Link).LinkTarget);
        Assert.False(Directory.Exists(files.Object.Root + ".new"));
        feed.Verify(f => f.Add(Path.Combine(directory.Combine("payload"), "nuget"), state), Times.Once);
    }

    [Fact]
    public async Task Install_ANewerHostThanTheOneThere_ReplacesTheMuxerSoItStaysTheNewest()
    {
        directory.Write("usr/share/dotnet/host/fxr/9.0.0/libhostfxr.so", "their fxr");
        directory.Write("usr/share/dotnet/dotnet", "their muxer");

        await bundle.Install(Install());

        Assert.Equal("muxer11", File.ReadAllText(Root("dotnet")));
    }

    [Fact]
    public async Task Install_AnSdkOfTheSameVersionIsThere_OnlyTheMissingOneIsAdded_AndTheirFilesStay()
    {
        directory.Write("usr/share/dotnet/sdk/11.0.100/sdk.dll", "theirs");
        directory.Write("usr/share/dotnet/host/fxr/11.0.0/libhostfxr.so", "their fxr");
        directory.Write("usr/share/dotnet/dotnet", "their muxer");
        sdks.Setup(s => s.InstalledVersions(It.IsAny<CancellationToken>())).ReturnsAsync(["11.0.100"]);

        await bundle.Install(Install());

        Assert.Equal("theirs", File.ReadAllText(Root("sdk", "11.0.100", "sdk.dll")));
        Assert.True(File.Exists(Root("sdk", "10.0.404", "sdk.dll")));
        Assert.Equal("their muxer", File.ReadAllText(Root("dotnet")));
        Assert.Contains(Path.Combine("sdk", "10.0.404"), state.Lines("dotnet-added"));
        Assert.Contains(Path.Combine("host", "fxr", "10.0.15"), state.Lines("dotnet-added"));
        Assert.DoesNotContain(state.Lines("dotnet-added"), path => path.Contains("11.0"));
    }

    [Fact]
    public async Task Install_OlderSdkOfAChannelIsThere_TheNewOneIsAddedBesideIt()
    {
        directory.Write("usr/share/dotnet/sdk/10.0.403/sdk.dll", "old");
        sdks.Setup(s => s.InstalledVersions(It.IsAny<CancellationToken>())).ReturnsAsync(["10.0.403"]);

        await bundle.Install(Install());

        Assert.True(File.Exists(Root("sdk", "10.0.403", "sdk.dll")));
        Assert.True(File.Exists(Root("sdk", "10.0.404", "sdk.dll")));
        Assert.True(File.Exists(Root("sdk", "11.0.100", "sdk.dll")));
    }

    [Fact]
    public async Task Install_BothSdksAreThereAlready_ChangesNothing()
    {
        directory.Write("usr/share/dotnet/sdk/10.0.404/sdk.dll", "a");
        sdks.Setup(s => s.InstalledVersions(It.IsAny<CancellationToken>())).ReturnsAsync(["10.0.404", "11.0.100"]);

        await bundle.Install(Install());

        Assert.False(File.Exists(Root("sdk", "11.0.100", "sdk.dll")));
        Assert.Empty(state.Lines("dotnet-added"));
    }

    [Fact]
    public async Task Uninstall_RemovesOnlyWhatTheInstallAdded()
    {
        directory.Write("usr/share/dotnet/sdk/9.0.100/sdk.dll", "theirs");
        directory.Write("usr/share/dotnet/host/fxr/9.0.0/libhostfxr.so", "their fxr");
        directory.Write("usr/share/dotnet/dotnet", "their muxer");
        await bundle.Install(Install());

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.False(Directory.Exists(Root("sdk", "10.0.404")));
        Assert.False(Directory.Exists(Root("sdk", "11.0.100")));
        Assert.False(Directory.Exists(Root("shared")));
        Assert.Equal("theirs", File.ReadAllText(Root("sdk", "9.0.100", "sdk.dll")));
        Assert.Equal("their fxr", File.ReadAllText(Root("host", "fxr", "9.0.0", "libhostfxr.so")));
        Assert.False(Directory.Exists(Root("host", "fxr", "11.0.0")));
        Assert.True(File.Exists(Root("dotnet")));
    }

    [Fact]
    public async Task Uninstall_AfterAnInstallIntoAnEmptyComputer_RemovesTheFolderAndTheLink()
    {
        await bundle.Install(Install());

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.False(Directory.Exists(files.Object.Root));
        Assert.Null(new FileInfo(files.Object.Link).LinkTarget);
    }

    [Fact]
    public async Task Uninstall_ALinkThatWasThereBefore_IsLeftAlone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(files.Object.Link)!);
        File.CreateSymbolicLink(files.Object.Link, "/somewhere/else/dotnet");
        await bundle.Install(Install());

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.Equal("/somewhere/else/dotnet", new FileInfo(files.Object.Link).LinkTarget);
    }

    [Fact]
    public async Task Uninstall_AnInstallRecordedByTheOlderOneSdkVersion_RemovesTheWholeFolder()
    {
        directory.Write("usr/share/dotnet/sdk/10.0.403/sdk.dll", "old");
        state.MarkOwned("dotnet");

        await bundle.Uninstall(new() { State = state, Users = [] });

        Assert.False(Directory.Exists(files.Object.Root));
    }
}
