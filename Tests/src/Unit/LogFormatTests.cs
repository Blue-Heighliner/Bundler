namespace BlueHeighliner.Bundler.Tests;

public sealed class LogFormatTests
{
    private readonly LogFormat format = new();

    [Theory]
    [InlineData("Step", "Building git")]
    [InlineData("Info", "detail line")]
    [InlineData("Warning", "careful")]
    [InlineData("Error", "broken")]
    public void Format_ThenParse_ReturnsTheSameLine(string levelName, string text)
    {
        LogLevel level = Enum.Parse<LogLevel>(levelName);
        LogLine parsed = format.Parse(format.Format(new() { Level = level, Text = text }));

        Assert.Equal(level, parsed.Level);
        Assert.Equal(text, parsed.Text);
    }

    [Fact]
    public void Parse_UnmarkedText_IsInfo()
    {
        LogLine parsed = format.Parse("plain output");

        Assert.Equal(LogLevel.Info, parsed.Level);
        Assert.Equal("plain output", parsed.Text);
    }

    [Fact]
    public void ObservableLog_RaisesWritten()
    {
        ObservableLog log = new();
        List<LogLine> lines = [];
        log.Written += lines.Add;

        log.Warn("hello");

        Assert.Equal(LogLevel.Warning, Assert.Single(lines).Level);
    }
}
