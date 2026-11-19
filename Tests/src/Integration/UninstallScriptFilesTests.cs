namespace BlueHeighliner.Bundler.Tests;

public sealed class UninstallScriptFilesTests : IDisposable
{
    private readonly UninstallHarness harness = new();
    private readonly TempDirectory directory = new();

    private BundleState State => new(directory.Combine("state", "bundle"));

    public void Dispose()
    {
        harness.Dispose();
        directory.Dispose();
    }

    private async Task<string> Run(Action<UninstallScript> steps)
    {
        UninstallScript script = new();
        steps(script);
        return await harness.Run(State, script);
    }

    [Fact]
    public async Task JsonUnset_RemovesOnlyValuesStillAsSet()
    {
        string path = directory.Write("s.json", "{ \"a\": 1, \"b\": 99, \"c\": 3 }");

        await Run(script => script.Add("json-unset", "alice", path, "{\"a\":1,\"b\":2}"));

        JsonNode saved = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Null(saved["a"]);
        Assert.Equal(99, saved["b"]!.GetValue<int>());
        Assert.Equal(3, saved["c"]!.GetValue<int>());
        Assert.Contains("chown -h alice:", harness.Calls.Single(call => call.StartsWith("chown", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task JsonUnset_ReadsCommentsAndTrailingCommasAndKeepsABackupOfTheOriginal()
    {
        string path = directory.Write("s.json", "{\n  // keep\n  \"a\": \"x // not a comment\", /* gone */\n  \"b\": [1, 2,],\n}\n");

        await Run(script => script.Add("json-unset", "alice", path, "{\"a\":\"x // not a comment\"}"));

        Assert.Equal([1, 2], JsonNode.Parse(File.ReadAllText(path))!["b"]!.AsArray().Select(item => item!.GetValue<int>()));
        Assert.Null(JsonNode.Parse(File.ReadAllText(path))!["a"]);
        Assert.Contains("// keep", File.ReadAllText(path + ".bundler.bak"));
    }

    [Fact]
    public async Task JsonUnset_EmptiedFile_IsDeleted()
    {
        string path = directory.Write("s.json", "{ \"a\": 1 }");

        await Run(script => script.Add("json-unset", "alice", path, "{\"a\":1}"));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task JsonUnset_NothingMatches_LeavesTheFileUntouched()
    {
        string path = directory.Write("s.json", "{ \"a\": 2 }  ");

        await Run(script => script.Add("json-unset", "alice", path, "{\"a\":1}"));

        Assert.Equal("{ \"a\": 2 }  ", File.ReadAllText(path));
        Assert.DoesNotContain(harness.Calls, call => call.StartsWith("chown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JsonUnset_MissingOrUnreadableFile_IsLeftAlone()
    {
        string broken = directory.Write("broken.json", "{ nope");

        string output = await Run(script =>
        {
            script.Add("json-unset", "alice", directory.Combine("none.json"), "{\"a\":1}");
            script.Add("json-unset", "alice", broken, "{\"a\":1}");
        });

        Assert.Equal("{ nope", File.ReadAllText(broken));
        Assert.Contains("not changed", output);
    }

    [Fact]
    public async Task BlockRemove_OnlyRemovesTheTaggedBlock()
    {
        string path = directory.Write("rc", "before\n# bundler one begin\n1\n# bundler one end\n# bundler two begin\n2\n# bundler two end\nafter\n");

        await Run(script => script.Add("block-remove", path, "one"));

        Assert.Equal(["before", "# bundler two begin", "2", "# bundler two end", "after"], File.ReadAllLines(path));
    }

    [Fact]
    public async Task BlockRemove_MissingFileOrBlock_ChangesNothing()
    {
        string path = directory.Write("rc", "plain\n");

        await Run(script =>
        {
            script.Add("block-remove", directory.Combine("none"), "tag");
            script.Add("block-remove", path, "tag");
        });

        Assert.False(File.Exists(directory.Combine("none")));
        Assert.Equal("plain\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task RestoreBackup_PutsTheOriginalBack_OnlyWhenTheFileIsTheBundlers()
    {
        string ours = directory.Write("ours/NuGet.Config", "<!-- bundler -->");
        directory.Write("ours/NuGet.Config.bundler.bak", "original");
        string theirs = directory.Write("theirs/NuGet.Config", "<configuration />");
        directory.Write("theirs/NuGet.Config.bundler.bak", "old");

        await Run(script =>
        {
            script.Add("restore-backup", ours, "bundler");
            script.Add("restore-backup", theirs, "bundler");
        });

        Assert.Equal("original", File.ReadAllText(ours));
        Assert.Equal("<configuration />", File.ReadAllText(theirs));
    }

    [Fact]
    public async Task ModelRemove_DeletesTheManifestAndOnlyTheBlobsNothingElseUses()
    {
        string models = directory.Combine("models");
        string digestA = new('a', 64);
        string digestB = new('b', 64);
        string manifest = directory.Write("models/manifests/registry/library/m/latest", $"sha256:{digestA} sha256:{digestB}");
        directory.Write("models/manifests-v2/registry/library/m/latest", "copy");
        directory.Write("models/manifests/registry/library/other/latest", $"sha256:{digestB}");
        directory.Write($"models/blobs/sha256-{digestA}", "a");
        directory.Write($"models/blobs/sha256-{digestB}", "b");
        directory.Write($"models/metadata/sha256-{digestA}.json", "{}");

        await Run(script => script.Add("model-remove", models, manifest));

        Assert.False(File.Exists(manifest));
        Assert.False(File.Exists(directory.Combine("models", "manifests-v2", "registry", "library", "m", "latest")));
        Assert.False(File.Exists(directory.Combine("models", "blobs", $"sha256-{digestA}")));
        Assert.False(File.Exists(directory.Combine("models", "metadata", $"sha256-{digestA}.json")));
        Assert.True(File.Exists(directory.Combine("models", "blobs", $"sha256-{digestB}")));
    }

    [Fact]
    public async Task Removals_DeleteWhatTheyNameAndNothingElse()
    {
        directory.Write("a/file", "x");
        directory.Write("tree/deep/file", "x");
        directory.Write("extensions/pub.ext-1.0.0/f", "x");
        directory.Write("extensions/pub.ext-pack-2.0.0/f", "x");
        directory.Write("parents/one/two/file", "x");
        File.Delete(directory.Combine("parents", "one", "two", "file"));
        Directory.CreateDirectory(directory.Combine("empty"));
        directory.Write("full/file", "x");
        Directory.CreateSymbolicLink(directory.Combine("link"), directory.Combine("opt", "tool", "1.0"));
        Directory.CreateSymbolicLink(directory.Combine("other-link"), "/usr/bin/else");
        Directory.CreateSymbolicLink(directory.Combine("any-link"), "/usr/bin/anything");

        await Run(script =>
        {
            script.Remove(directory.Combine("a", "file"));
            script.RemoveTree(directory.Combine("tree"));
            script.Add("rm-glob", directory.Combine("extensions"), "pub.ext-[0-9]*");
            script.Add("rm-empty-parents", directory.Combine("parents", "one", "two", "file"), directory.Path);
            script.RemoveEmpty(directory.Combine("empty"));
            script.RemoveEmpty(directory.Combine("full"));
            script.RemoveLink(directory.Combine("link"), directory.Combine("opt", "tool"));
            script.RemoveLink(directory.Combine("other-link"), directory.Combine("opt", "tool"));
            script.RemoveLink(directory.Combine("any-link"));
            script.Add("rm-path", directory.Combine("a"));
        });

        Assert.False(Directory.Exists(directory.Combine("a")));
        Assert.False(Directory.Exists(directory.Combine("tree")));
        Assert.False(Directory.Exists(directory.Combine("extensions", "pub.ext-1.0.0")));
        Assert.True(Directory.Exists(directory.Combine("extensions", "pub.ext-pack-2.0.0")));
        Assert.False(Directory.Exists(directory.Combine("parents")));
        Assert.False(Directory.Exists(directory.Combine("empty")));
        Assert.True(File.Exists(directory.Combine("full", "file")));
        Assert.Null(new FileInfo(directory.Combine("link")).LinkTarget);
        Assert.False(File.Exists(directory.Combine("link")));
        Assert.Equal("/usr/bin/else", new FileInfo(directory.Combine("other-link")).LinkTarget);
        Assert.Null(new FileInfo(directory.Combine("any-link")).LinkTarget);
    }

    [Fact]
    public async Task Conditions_RunTheirStepsOnlyWhenTheyHold()
    {
        directory.Write("with/deep/file", "x");
        Directory.CreateDirectory(directory.Combine("without"));
        directory.Write("marker-a", "x");
        directory.Write("marker-b", "x");
        directory.Write("marker-c", "x");

        await Run(script =>
        {
            script.IfFiles(true, [directory.Combine("with"), directory.Combine("without")], then => then.Remove(directory.Combine("marker-a")));
            script.IfFiles(true, [directory.Combine("without")], then => then.Remove(directory.Combine("marker-b")));
            script.IfFiles(false, [directory.Combine("without")], then => then.Remove(directory.Combine("marker-c")));
            script.IfSystemd(then => then.Info("systemd branch"));
        });

        Assert.False(File.Exists(directory.Combine("marker-a")));
        Assert.True(File.Exists(directory.Combine("marker-b")));
        Assert.False(File.Exists(directory.Combine("marker-c")));
    }

    [Fact]
    public async Task Commands_RunWithTheirArgumentsAndAMissingProgramIsNotAnError()
    {
        string output = await Run(script =>
        {
            script.Info("it's quoted 'right'");
            script.Warn("careful");
            script.Add("run", "usermod", "--del-subuids", "100000-165535", "alice");
            script.Add("run", "no-such-program-here", "x");
            script.Add("run-as", "alice", "code", "--uninstall-extension", "a.b");
            script.Add("systemctl", "disable --now", "chroma-db");
            script.Add("daemon-reload");
            script.Add("disable-linger", "alice");
        });

        Assert.Contains("it's quoted 'right'", output);
        Assert.Contains("warning: careful", output);
        Assert.Contains("usermod --del-subuids 100000-165535 alice", harness.Calls);
        Assert.Contains(harness.Calls, call => call.StartsWith("runuser -u alice -- env HOME=", StringComparison.Ordinal) && call.EndsWith("code --uninstall-extension a.b", StringComparison.Ordinal));
        Assert.Contains("loginctl disable-linger alice", harness.Calls);
    }

    [Fact]
    public async Task GitUnset_UnsetsADefaultOnlyWhenItStillHasTheValueTheBundleSet()
    {
        harness.Git("init.defaultBranch", "main");
        harness.Git("core.editor", "vim");

        await Run(script =>
        {
            script.Add("git-unset", "init.defaultBranch", "main");
            script.Add("git-unset", "core.editor", "code --wait");
        });

        Assert.False(harness.HasGit("init.defaultBranch"));
        Assert.True(harness.HasGit("core.editor"));
    }
}
