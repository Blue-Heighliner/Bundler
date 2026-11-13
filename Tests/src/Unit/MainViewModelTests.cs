namespace BlueHeighliner.Bundler.Tests;

public sealed class MainViewModelTests
{
    private readonly Mock<IBundleEngine> engine = new();
    private readonly Mock<IMachine> machine = new();
    private readonly Mock<IDialogs> dialogs = new();
    private readonly Mock<IBundleServices> services = new();
    private readonly ObservableLog log = new();
    private readonly MainViewModel model;

    public MainViewModelTests()
    {
        machine.SetupGet(m => m.IsLinux).Returns(true);
        services.Setup(x => x.For("git")).Returns([new ServiceRow("git", "2.56.0", "Source control", ["GPL-2.0"], "Clear.")]);
        services.Setup(x => x.For("dev")).Returns([]);
        engine.Setup(e => e.List()).Returns(
        [
            new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0) },
            new BundleStatus { Name = "dev", Description = "Dev", Target = "Ubuntu 24.04", Requires = ["git"], State = BundleStanding.NotBuilt },
        ]);
        engine.Setup(e => e.InstallPlan(It.IsAny<IReadOnlyList<string>>())).Returns((IReadOnlyList<string> names) => names);
        dialogs.Setup(d => d.Confirm(It.IsAny<string>())).ReturnsAsync(true);
        model = new(engine.Object, machine.Object, services.Object, new ByteSizes(), log, dialogs.Object, new ImmediateDispatcher());
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action work)
            => work();
    }

    [Fact]
    public async Task Start_LoadsTheRows()
    {
        await model.Start();

        Assert.Equal(["git", "dev"], model.Bundles.Select(row => row.Name));
        Assert.Equal("available", model.Bundles[0].Badge);
        Assert.Equal("2026-10-30 09:19", model.Bundles[0].BundleFile);
        Assert.Equal("missing", model.Bundles[1].Badge);
        Assert.Equal("None in the Bundles folder", model.Bundles[1].BundleFile);
        Assert.Equal("Ubuntu 24.04", model.Bundles[0].Target);
        Assert.Equal("git", model.Bundles[1].Dependencies);
        Assert.Equal("None", model.Bundles[0].Dependencies);
        Assert.Equal("Source control", Assert.Single(model.Bundles[0].Services).Description);
    }

    [Fact]
    public async Task Start_ShowsTheFirstBundleAndKeepsTheShownOneAcrossRefreshes()
    {
        await model.Start();
        Assert.Equal("git", model.Current!.Name);
        Assert.True(model.HasCurrent);

        model.Current = model.Bundles[1];
        await model.Refresh();

        Assert.Equal("dev", model.Current!.Name);
    }

    [Theory]
    [InlineData("NotBuilt", "missing")]
    [InlineData("Available", "available")]
    [InlineData("Installed", "installed")]
    public void Row_ShowsTheBadge(string state, string badge)
    {
        BundleRowViewModel row = new(new BundleStatus { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = Enum.Parse<BundleStanding>(state) }, [], false);

        Assert.Equal(badge, row.Badge);
        Assert.Equal(char.ToUpperInvariant(badge[0]) + badge[1..], row.Kind);
    }

    [Fact]
    public async Task Activity_NamesTheRunningActionThenReturnsToReady()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        TaskCompletionSource release = new();
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).Returns(release.Task);

        Task running = model.Build();
        await Task.Delay(50);
        Assert.Equal("Building", model.Activity);
        release.SetResult();
        await running;

        Assert.Equal("Ready", model.Activity);
    }

    [Fact]
    public async Task Reinstall_AsksThenReinstallsTheTickedBundles()
    {
        await model.Start();
        model.SelectAll(true);

        await model.Reinstall();

        dialogs.Verify(d => d.Confirm(It.Is<string>(question => question.Contains("Reinstall git, dev"))), Times.Once);
        engine.Verify(e => e.Reinstall(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        engine.Verify(e => e.Uninstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_Declined_DoesNothing()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        dialogs.Setup(d => d.Confirm(It.IsAny<string>())).ReturnsAsync(false);

        await model.Reinstall();

        engine.Verify(e => e.Reinstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_WhileBusyOrWithNothingTicked_DoesNothing()
    {
        await model.Start();

        await model.Reinstall();

        dialogs.Verify(d => d.Confirm(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reinstall_NamesTheActivity()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        TaskCompletionSource release = new();
        engine.Setup(e => e.Reinstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>())).Returns(release.Task);

        Task running = model.Reinstall();
        await Task.Delay(50);
        Assert.Equal("Reinstalling", model.Activity);
        Assert.False(model.CanInstall);
        release.SetResult();
        await running;

        Assert.Equal("Ready", model.Activity);
    }

    [Fact]
    public async Task ShowLicense_OpensTheLicensesOfTheService()
    {
        await model.ShowLicense(new LicenseLink("git", "MIT", ["GPL-2.0", "MIT"]));

        dialogs.Verify(d => d.ShowLicenses("git", It.Is<IReadOnlyList<string>>(all => all.SequenceEqual(new[] { "GPL-2.0", "MIT" })), "MIT"), Times.Once);
    }

    [Fact]
    public void ClearLog_EmptiesTheLog()
    {
        log.Info("x");
        model.ClearLog();

        Assert.Empty(model.Log);
    }

    [Fact]
    public async Task Buttons_NeedASelection()
    {
        await model.Start();
        Assert.False(model.CanBuild);

        model.Bundles[0].IsSelected = true;

        Assert.True(model.CanBuild);
        Assert.True(model.CanInstall);
    }

    [Fact]
    public async Task SelectAll_TicksEveryRow()
    {
        await model.Start();

        model.SelectAll(true);

        Assert.All(model.Bundles, row => Assert.True(row.IsSelected));
        model.SelectAll(false);
        Assert.All(model.Bundles, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public async Task Refresh_KeepsTheTicks()
    {
        await model.Start();
        model.Bundles[1].IsSelected = true;

        await model.Refresh();

        Assert.True(model.Bundles[1].IsSelected);
        Assert.False(model.Bundles[0].IsSelected);
    }

    [Fact]
    public async Task Build_RunsTheEngineOnAllTickedBundles()
    {
        await model.Start();
        model.SelectAll(true);

        await model.Build();

        engine.Verify(e => e.Build(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Build_Failure_GoesToTheLog()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no network"));

        await model.Build();

        Assert.Contains(model.Log, entry => entry.Level == LogLevel.Error && entry.Text == "no network");
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Build_Cancelled_IsLoggedAsAWarning()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await model.Build();

        Assert.Contains(model.Log, entry => entry.Level == LogLevel.Warning && entry.Text == "Build cancelled");
    }

    [Fact]
    public async Task OnlyOneActionRunsAtATime()
    {
        await model.Start();
        model.SelectAll(true);
        TaskCompletionSource release = new();
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).Returns(release.Task);

        Task running = model.Build();
        await Task.Delay(50);

        Assert.True(model.IsBusy);
        Assert.False(model.CanBuild);
        Assert.False(model.CanInstall);
        Assert.False(model.CanRefresh);
        await model.Install();
        await model.Uninstall();
        await model.Build();
        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        engine.Verify(e => e.Uninstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);

        release.SetResult();
        await running;
        Assert.False(model.IsBusy);
        Assert.True(model.CanBuild);
    }

    [Fact]
    public async Task Cancel_StopsTheRunningAction()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        engine.Setup(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).Returns(async (IReadOnlyList<string> _, long? _, CancellationToken token) => await Task.Delay(Timeout.Infinite, token));

        Task running = model.Build();
        await Task.Delay(50);
        model.Cancel();
        await running;

        Assert.Contains(model.Log, entry => entry.Text == "Build cancelled");
    }

    [Fact]
    public async Task Install_AsksThenCallsTheEngineWithTheOptions()
    {
        await model.Start();
        model.SelectAll(true);

        await model.Install();

        dialogs.Verify(d => d.Confirm(It.Is<string>(question => question.Contains("git, dev"))), Times.Once);
        engine.Verify(e => e.Install(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git", "dev" })), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Install_Declined_DoesNothing()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        dialogs.Setup(d => d.Confirm(It.IsAny<string>())).ReturnsAsync(false);

        await model.Install();

        engine.Verify(e => e.Install(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Uninstall_AsksThenCallsTheEngine()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;

        await model.Uninstall();

        engine.Verify(e => e.Uninstall(It.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "git" })), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Uninstall_Failure_IsLogged()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        engine.Setup(e => e.Uninstall(It.IsAny<IReadOnlyList<string>>(), It.IsAny<RunOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("dev needs git"));

        await model.Uninstall();

        Assert.Contains(model.Log, entry => entry.Level == LogLevel.Error && entry.Text == "dev needs git");
    }

    [Fact]
    public async Task LogLines_AreShownAndCapped()
    {
        await model.Start();

        for (int index = 0; index < 5100; index++)
        {
            log.Info($"line {index}");
        }

        Assert.Equal(5000, model.Log.Count);
        Assert.Equal("line 5099", model.Log[^1].Text);
    }

    [Fact]
    public async Task NotOnLinux_BuildsAreDisabledAndANoticeIsShown()
    {
        machine.SetupGet(m => m.IsLinux).Returns(false);
        await model.Start();
        model.SelectAll(true);

        Assert.False(model.CanBuild);
        Assert.NotEmpty(model.Notice);
    }

    [Theory]
    [InlineData("Step", "#569CD6")]
    [InlineData("Warning", "#E2C08D")]
    [InlineData("Error", "#F48771")]
    [InlineData("Info", "#CCCCCC")]
    public void LogEntry_ColorsByLevel(string level, string expected)
    {
        LogEntry entry = new() { Text = "x", Level = Enum.Parse<LogLevel>(level) };

        Assert.Equal(Avalonia.Media.Color.Parse(expected), ((Avalonia.Media.ISolidColorBrush)entry.Foreground).Color);
    }

    [Fact]
    public async Task Build_WithAMaxSize_PassesTheParsedLimit()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        model.MaxSize = "4.7G";

        await model.Build();

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), 4_700_000_000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithoutAMaxSize_PassesNoLimit()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;

        await model.Build();

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Build_WithAMaxSizeThatIsNotASize_LogsTheErrorAndBuildsNothing()
    {
        await model.Start();
        model.Bundles[0].IsSelected = true;
        model.MaxSize = "lots";

        await model.Build();

        engine.Verify(e => e.Build(It.IsAny<IReadOnlyList<string>>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(model.Log, entry => entry.Level == LogLevel.Error && entry.Text.Contains("lots"));
        Assert.False(model.IsBusy);
    }

    [Fact]
    public void MaxSize_StartsEmptyAndNeverBecomesNull()
    {
        Assert.Equal(string.Empty, model.MaxSize);

        model.MaxSize = null!;

        Assert.Equal(string.Empty, model.MaxSize);
    }

    [Fact]
    public void BundleFile_TellsThePartsAndWhatIsWrongWithThem()
    {
        BundleStatus status = new() { Name = "git", Description = "Git", Target = "Ubuntu 24.04", Requires = [], State = BundleStanding.Available, FileModified = new DateTime(2026, 10, 30, 9, 19, 0) };

        Assert.Equal("2026-10-30 09:19", new BundleRowViewModel(status, [], false).BundleFile);
        Assert.Equal("2026-10-30 09:19, 4 parts", new BundleRowViewModel(status with { FileCount = 4 }, [], false).BundleFile);
        Assert.Equal("2026-10-30 09:19, 4 parts, incomplete: part 2 is missing", new BundleRowViewModel(status with { FileCount = 4, FileProblem = "part 2 is missing" }, [], false).BundleFile);
    }
}
