namespace BlueHeighliner.Bundler.Tests;

public sealed class OllamaConfigTests
{
    private readonly Resources resources = new();

    [Theory]
    [InlineData("ollama/ollama.service")]
    [InlineData("ollama/ollama-bundler.conf")]
    public void SystemdFiles_HaveNoBackslashEscapesThatSystemdWouldMangle(string name)
        => Assert.DoesNotContain('\\', resources.Text(name));

    [Fact]
    public void GpuWait_LooksForTheNvidiaVendorIdWithAPatternGrepAccepts()
    {
        string line = resources.Text("ollama/ollama-bundler.conf").Split('\n').Single(entry => entry.StartsWith("ExecStartPre=", StringComparison.Ordinal));

        Assert.Contains("grep -qi \"[[]10de:\"", line);
    }
}
