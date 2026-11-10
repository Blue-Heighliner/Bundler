namespace BlueHeighliner.Bundler.Tests;

public sealed class AutoRemoteFilesTests
{
    private readonly AutoRemoteFiles files = new();

    [Fact]
    public void Unit_IsTheServersUserServiceOfTheAuthorsMachine()
    {
        string unit = files.UnitText;

        Assert.Contains("Description=Auto-Remote server", unit);
        Assert.Contains("After=network-online.target", unit);
        Assert.Contains("ExecStart=/opt/auto-remote/server/Markwardt.AutoRemote.Server %h/.config/auto-remote/config.json", unit);
        Assert.Contains("WorkingDirectory=%h", unit);
        Assert.Contains("Restart=on-failure", unit);
        Assert.Contains("RestartSec=5", unit);
        Assert.Contains("WantedBy=default.target", unit);
        Assert.EndsWith("\n", unit);
    }

    [Fact]
    public void DesktopEntry_PutsTheClientInTheAppFinderWithItsIcon()
    {
        string entry = files.DesktopEntryText;

        Assert.StartsWith("[Desktop Entry]\n", entry);
        Assert.Contains("Name=Auto Remote", entry);
        Assert.Contains("Exec=/opt/auto-remote/desktop/Markwardt.AutoRemote.DesktopClient", entry);
        Assert.Contains("Icon=/usr/share/icons/hicolor/1024x1024/apps/auto-remote.png", entry);
        Assert.Contains("Terminal=false", entry);
        Assert.Contains("Categories=Development;", entry);
        Assert.Contains("StartupWMClass=Markwardt.AutoRemote.DesktopClient", entry);
    }

    [Fact]
    public void Paths_AreUnderTheInstallFolder()
    {
        Assert.Equal("/opt/auto-remote/server/Markwardt.AutoRemote.Server", files.Server);
        Assert.Equal("/opt/auto-remote/desktop/Markwardt.AutoRemote.DesktopClient", files.Client);
        Assert.Equal("/opt/auto-remote/version.txt", files.VersionFile);
    }

    [Theory]
    [InlineData(0, 52679)]
    [InlineData(1, 52680)]
    [InlineData(3, 52682)]
    public void PortFor_CountsUpFromTheDefault(int position, int port)
        => Assert.Equal(port, files.PortFor(position));

    [Fact]
    public void Config_OmitsTheDefaultPortAndKeepsAnotherOne()
    {
        JsonNode first = JsonNode.Parse(files.ConfigText("abc", null))!;
        JsonNode explicitDefault = JsonNode.Parse(files.ConfigText("abc", 52679))!;
        JsonNode second = JsonNode.Parse(files.ConfigText("abc", 52680))!;

        Assert.Equal("abc", first["key"]!.GetValue<string>());
        Assert.Null(first["port"]);
        Assert.Null(explicitDefault["port"]);
        Assert.Equal(52680, second["port"]!.GetValue<int>());
        Assert.EndsWith("\n", files.ConfigText("abc", null));
    }

    [Fact]
    public void NewKey_IsLongRandomAndSafeInAFileAndAUrl()
    {
        string[] keys = [.. Enumerable.Range(0, 20).Select(_ => files.NewKey())];

        Assert.Equal(20, keys.Distinct().Count());
        Assert.All(keys, key =>
        {
            Assert.Equal(32, key.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", key);
        });
    }
}
