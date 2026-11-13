namespace BlueHeighliner.Bundler.Tests;

public sealed class CommandLineTests
{
    private readonly Mock<IBundleEngine> engine = new();
    private readonly Mock<ILog> log = new();
    private readonly CommandLine commandLine;

    public CommandLineTests()
    {
        engine.Setup(e => e.List()).Returns([new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0) }]);
        commandLine = new(engine.Object, new ByteSizes(), log.Object);
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
        Assert.Contains("Ubuntu 24.04", output.ToString());
    }

    [Fact]
    public async Task Build_PassesTheNames()
    {
        Assert.Equal(0, await commandLine.Run(["build", "git", "dev"]));

        engine.Verify(e => e.Build(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Once);
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
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).Returns(() =>
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
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no network"));

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

    [Theory]
    [InlineData("--max-size", "4.7G", 4_700_000_000)]
    [InlineData("--max-size", "700M", 700_000_000)]
    [InlineData("--max-size=4700000000", null, 4_700_000_000)]
    [InlineData("--max-size=4GiB", null, 4_294_967_296)]
    public async Task Build_WithAMaxSize_PassesTheLimitToTheEngine(string option, string? value, long expected)
    {
        string[] arguments = value is null ? ["build", "git", option] : ["build", option, value, "git"];

        Assert.Equal(0, await commandLine.Run(arguments));

        engine.Verify(e => e.Build(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git" })), expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithoutAMaxSize_PassesNoLimit()
    {
        await commandLine.Run(["build", "git"]);

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("--max-size", "huge")]
    [InlineData("--max-size=huge", null)]
    [InlineData("--max-size=", null)]
    public async Task Build_WithABadMaxSize_IsAUsageErrorAndBuildsNothing(string option, string? value)
    {
        string[] arguments = value is null ? ["build", "git", option] : ["build", "git", option, value];

        Assert.Equal(2, await commandLine.Run(arguments));

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        log.Verify(l => l.Write(LogLevel.Error, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Build_MaxSizeWithoutAValue_IsAUsageError()
    {
        Assert.Equal(2, await commandLine.Run(["build", "git", "--max-size"]));

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Help_DescribesTheMaxSizeOption()
        => Assert.Contains("--max-size", commandLine.Help);

    [Fact]
    public async Task List_ShowsHowManyPartsABundleIsIn()
    {
        engine.Setup(e => e.List()).Returns([new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0), FileCount = 7 }, new BundleStatus { Name = "dev", Description = "Dev", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0), FileCount = 3, FileProblem = "part 2 is missing" }]);
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            await commandLine.Run(["list"]);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("2026-10-30 09:19 (7 parts)", output.ToString());
        Assert.Contains("(3 parts) INCOMPLETE", output.ToString());
    }
}
