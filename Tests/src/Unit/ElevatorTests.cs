namespace BlueHeighliner.Bundler.Tests;

public sealed class ElevatorTests
{
    private readonly Mock<IMachine> machine = new();
    private readonly Mock<IPaths> paths = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<ILog> log = new();
    private readonly Elevator elevator;
    private ProcessCommand? command;

    public ElevatorTests()
    {
        machine.SetupGet(m => m.SelfCommand).Returns(["/app/Bundler"]);
        machine.Setup(m => m.FindExecutable(It.IsAny<string>())).Returns("/usr/bin/x");
        paths.SetupGet(p => p.Bundles).Returns("/app/bundles");
        machine.SetupGet(m => m.DisplayEnvironment).Returns(new Dictionary<string, string> { ["DISPLAY"] = ":0" });
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).Callback<ProcessCommand, CancellationToken>((c, _) => command = c).ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty));
        elevator = new(machine.Object, paths.Object, runner.Object, new LogFormat(), log.Object);
    }

    [Fact]
    public async Task Run_InATerminal_UsesSudoAndSharesTheTerminal()
    {
        await elevator.Run(["install", "git"], useTerminal: true);

        Assert.Equal("sudo", command!.FileName);
        Assert.True(command.Inherit);
        Assert.Equal(["env", "DISPLAY=:0", "/app/Bundler", "install", "git", "--bundles", "/app/bundles"], command.Arguments);
    }

    [Fact]
    public async Task Run_FromTheWindow_UsesPkexecAndReadsTheOutput()
    {
        await elevator.Run(["install", "git"], useTerminal: false);

        Assert.Equal("pkexec", command!.FileName);
        Assert.False(command.Inherit);
        command.OnLine!("==> Installing git");
        log.Verify(l => l.Write(LogLevel.Step, "Installing git"), Times.Once);
    }

    [Fact]
    public async Task Run_WithoutPkexec_FallsBackToSudo()
    {
        machine.Setup(m => m.FindExecutable("pkexec")).Returns((string?)null);

        await elevator.Run(["install", "git"], useTerminal: false);

        Assert.Equal("sudo", command!.FileName);
    }

    [Fact]
    public async Task Run_NoWayToElevate_Throws()
    {
        machine.Setup(m => m.FindExecutable(It.IsAny<string>())).Returns((string?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => elevator.Run(["install"], useTerminal: true));
    }

    [Fact]
    public async Task Run_ReturnsTheExitCode()
    {
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProcessResult(126, string.Empty, string.Empty));

        Assert.Equal(126, await elevator.Run(["install"], useTerminal: true));
    }
}
