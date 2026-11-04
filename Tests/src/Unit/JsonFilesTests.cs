namespace BlueHeighliner.Bundler.Tests;

public sealed class JsonFilesTests
{
    private readonly JsonFiles json = new();

    [Fact]
    public void Set_CreatesTheFile()
    {
        using TempDirectory directory = new();
        string path = directory.Combine("sub", "settings.json");

        Assert.True(json.Set(path, new() { ["a"] = 1 }));

        Assert.Equal(1, JsonNode.Parse(File.ReadAllText(path))!["a"]!.GetValue<int>());
    }

    [Fact]
    public void Set_MergesAndKeepsOtherKeys()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{ \"keep\": true }");

        json.Set(path, new() { ["a"] = "x" });

        JsonNode saved = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.True(saved["keep"]!.GetValue<bool>());
        Assert.Equal("x", saved["a"]!.GetValue<string>());
    }

    [Fact]
    public void Set_SameValues_DoesNotRewrite()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{ \"a\": 1 }");

        Assert.False(json.Set(path, new() { ["a"] = 1 }));
    }

    [Fact]
    public void Set_SkipWhenPresent_LeavesTheFileAlone()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{ \"mine\": 1 }");

        Assert.False(json.Set(path, new() { ["a"] = 1, ["mine"] = 2 }, skipWhenPresent: "mine"));
        Assert.Equal("{ \"mine\": 1 }", File.ReadAllText(path));
    }

    [Fact]
    public void Set_FileWithCommentsAndTrailingCommas_IsParsedAndBackedUp()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{\n  // note\n  \"keep\": 1,\n}");

        json.Set(path, new() { ["a"] = 2 });

        Assert.True(File.Exists(path + ".bundler.bak"));
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(path))!["a"]!.GetValue<int>());
    }

    [Fact]
    public void Set_NotAnObject_Throws()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "[1, 2]");

        Assert.Throws<InvalidOperationException>(() => json.Set(path, new() { ["a"] = 1 }));
    }

    [Fact]
    public void UnsetMatching_RemovesOnlyValuesStillAsSet()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{ \"a\": 1, \"b\": 99, \"c\": 3 }");

        Assert.True(json.UnsetMatching(path, new() { ["a"] = 1, ["b"] = 2 }));

        JsonNode saved = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Null(saved["a"]);
        Assert.Equal(99, saved["b"]!.GetValue<int>());
    }

    [Fact]
    public void UnsetMatching_EmptiedFile_IsDeleted()
    {
        using TempDirectory directory = new();
        string path = directory.Write("s.json", "{ \"a\": 1 }");

        json.UnsetMatching(path, new() { ["a"] = 1 });

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UnsetMatching_MissingFile_ReturnsFalse()
    {
        using TempDirectory directory = new();

        Assert.False(json.UnsetMatching(directory.Combine("none.json"), new() { ["a"] = 1 }));
    }
}
