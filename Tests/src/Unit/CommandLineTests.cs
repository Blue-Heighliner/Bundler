namespace BlueHeighliner.Bundler.Tests;

public sealed class CommandLineTests
{
    private readonly Mock<IBundleEngine> engine = new();
    private readonly Mock<ILog> log = new();
    private readonly CommandLine commandLine;

    public CommandLineTests()
    {
        engine.Setup(e => e.List()).Returns([new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 26.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0) }]);
        commandLine = new(engine.Object, log.Object);
    }

    [Fact]
    public async Task List_ShowsTheBundles()
    {
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.Equal(0, await commandLine.Run(["list"]));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("git", output.ToString());
        Assert.Contains("2026-10-30 09:19", output.ToString());
        Assert.Contains("BUNDLE FILE", output.ToString());
        Assert.Contains("TARGET", output.ToString());
        Assert.Contains("Ubuntu 26.04", output.ToString());
    }

    [Fact]
    public async Task Build_PassesTheNames()
    {
        Assert.Equal(0, await commandLine.Run(["build", "git", "dev"]));

        engine.Verify(e => e.Build(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithoutNames_IsAUsageError()
    {
        Assert.Equal(2, await commandLine.Run(["build"]));
    }

    [Fact]
    public async Task Build_CancelledByTheUser_ReturnsOneThirtyWithoutAnError()
    {
        using CancellationTokenSource cancel = new();
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });

        Assert.Equal(130, await commandLine.Run(["build", "git"], cancel.Token));

        log.Verify(l => l.Write(LogLevel.Error, It.IsAny<string>()), Times.Never);
        log.Verify(l => l.Write(LogLevel.Warning, "cancelled"), Times.Once);
    }

    [Fact]
    public async Task Build_Failure_IsLoggedAndReturnsOne()
    {
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no network"));

        Assert.Equal(1, await commandLine.Run(["build", "git"]));

        log.Verify(l => l.Write(LogLevel.Error, "no network"), Times.Once);
    }

    [Fact]
    public async Task Install_CallsTheEngineWithOptions()
    {
        Assert.Equal(0, await commandLine.Run(["install", "git", "-y"]));

        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.Is<RunOptions>(options => options.Confirm == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_WithoutYes_AsksForConfirmation()
    {
        await commandLine.Run(["install", "git"]);

        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.Is<RunOptions>(options => options.Confirm != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reinstall_CallsTheEngine()
    {
        Assert.Equal(0, await commandLine.Run(["reinstall", "git", "dev", "-y"]));

        engine.Verify(e => e.Reinstall(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.Is<RunOptions>(options => options.Confirm == null), It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Force_IsNoLongerAnOption()
        => Assert.Equal(2, await commandLine.Run(["install", "git", "--force"]));

    [Fact]
    public async Task Remove_IsUninstall()
    {
        await commandLine.Run(["remove", "git", "-y"]);

        engine.Verify(e => e.Uninstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownCommandOrOption_IsAUsageError()
    {
        StringWriter errors = new();
        TextWriter original = Console.Error;
        Console.SetError(errors);
        try
        {
            Assert.Equal(2, await commandLine.Run(["frobnicate"]));
            Assert.Equal(2, await commandLine.Run(["build", "git", "--bogus"]));
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task Help_PrintsTheUsage()
    {
        StringWriter errors = new();
        TextWriter original = Console.Error;
        Console.SetError(errors);
        try
        {
            Assert.Equal(0, await commandLine.Run(["help"]));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains("install <bundle>", errors.ToString());
    }
}
