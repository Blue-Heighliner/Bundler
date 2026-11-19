namespace BlueHeighliner.Bundler.Tests;

public sealed class PythonWheelsTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly Mock<IProcessRunner> runner = new();
    private readonly Mock<IResources> resources = new();
    private readonly List<ProcessCommand> commands = [];
    private readonly Dictionary<string, string> metadata = [];
    private readonly PythonWheels wheels;
    private bool pipMissing;

    public PythonWheelsTests()
    {
        resources.Setup(r => r.Text("reviewed-python.tsv")).Returns("# reviewed\nweird-pkg\tGPL-3.0-only\n");
        runner.Setup(r => r.Run(It.IsAny<ProcessCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProcessCommand command, CancellationToken _) =>
        {
            commands.Add(command);
            if (command.Arguments.Contains("--version"))
            {
                return new ProcessResult(pipMissing ? 1 : 0, string.Empty, string.Empty);
            }

            string target = command.Arguments[command.Arguments.ToList().IndexOf("-d") + 1];
            foreach ((string name, string text) in metadata)
            {
                using ZipArchive zip = ZipFile.Open(Path.Combine(target, $"{name}-1.0-py3-none-any.whl"), ZipArchiveMode.Create);
                using StreamWriter writer = new(zip.CreateEntry($"{name}-1.0.dist-info/METADATA").Open());
                writer.Write($"Metadata-Version: 2.4\nName: {name}\nVersion: 1.0\n{text}\n\nThe description.\n");
            }

            return new ProcessResult(0, string.Empty, string.Empty);
        });
        wheels = new(runner.Object, new NuGetLicenses(resources.Object), resources.Object, Mock.Of<ILog>());
    }

    public void Dispose()
        => directory.Dispose();

    private async Task<PythonWheel> One(string name, string headers)
    {
        metadata[name] = headers;
        return (await wheels.Download(directory.Combine("w" + metadata.Count), ["x"])).First(wheel => wheel.Name == name);
    }

    [Theory]
    [InlineData("License-Expression: MIT", "MIT", false)]
    [InlineData("License-Expression: Apache-2.0 AND (BSD-3-Clause OR MIT)", "Apache-2.0 AND (BSD-3-Clause OR MIT)", false)]
    [InlineData("License: Apache License, Version 2.0", "Apache-2.0", false)]
    [InlineData("License: 3-Clause BSD License", "BSD-3-Clause", false)]
    [InlineData("License: PSF-2.0", "PSF-2.0", false)]
    [InlineData("Classifier: License :: OSI Approved :: Apache Software License", "Apache-2.0", false)]
    [InlineData("Classifier: License :: OSI Approved :: MIT License\nClassifier: License :: OSI Approved :: BSD License", "MIT AND BSD-3-Clause", false)]
    [InlineData("License-Expression: GPL-3.0-only", "GPL-3.0-only", true)]
    [InlineData("License: Something Proprietary", "Something Proprietary", true)]
    [InlineData("Summary: no license at all", "unknown", true)]
    public async Task Download_ReadsTheLicenseFromTheWheel(string headers, string expected, bool needsReview)
    {
        PythonWheel wheel = await One("pkg", headers);

        Assert.Equal(expected, wheel.License);
        Assert.Equal(needsReview, wheel.NeedsReview);
        Assert.Equal("1.0", wheel.Version);
        Assert.Equal("pkg-1.0-py3-none-any.whl", wheel.FileName);
    }

    [Fact]
    public async Task Download_ShortLicenseFieldIsPreferredOverClassifiersWhenThereIsNoExpression()
    {
        PythonWheel wheel = await One("pkg", "License: MIT License\nClassifier: License :: OSI Approved :: Apache Software License");

        Assert.Equal("MIT", wheel.License);
    }

    [Fact]
    public async Task Download_ALicenseThatWasReviewed_IsOkAndSaysSo()
    {
        PythonWheel wheel = await One("weird-pkg", "License-Expression: GPL-3.0-only");

        Assert.False(wheel.NeedsReview);
        Assert.Equal("GPL-3.0-only (reviewed)", wheel.License);
    }

    [Fact]
    public async Task Download_AsksPipForWheelsOfTheTargetsPythonOnly()
    {
        metadata["a"] = "License-Expression: MIT";

        await wheels.Download(directory.Combine("w"), ["chromadb", "numpy"]);

        ProcessCommand download = commands.Single(command => command.Arguments.Contains("download"));
        Assert.Equal("python3", download.FileName);
        Assert.Contains("--only-binary=:all:", download.Arguments);
        string text = string.Join(' ', download.Arguments);
        Assert.Contains("--python-version 3.12", text);
        Assert.Contains("--implementation cp", text);
        Assert.Contains("--abi cp312", text);
        Assert.Contains("--platform manylinux_2_17_x86_64", text);
        Assert.Contains("--platform manylinux_2_39_x86_64", text);
        Assert.DoesNotContain("manylinux_2_40", text);
        Assert.Contains("--platform manylinux2014_x86_64", text);
        Assert.Contains($"-d {directory.Combine("w")} chromadb numpy", text);
    }

    [Fact]
    public async Task Download_ReturnsTheWheelsInNameOrder()
    {
        metadata["zeta"] = "License-Expression: MIT";
        metadata["Alpha"] = "License-Expression: MIT";
        metadata["beta"] = "License-Expression: MIT";

        IReadOnlyList<PythonWheel> found = await wheels.Download(directory.Combine("w"), ["x"]);

        Assert.Equal(["Alpha", "beta", "zeta"], found.Select(wheel => wheel.Name));
    }

    [Fact]
    public async Task Download_PipMissing_Throws()
    {
        pipMissing = true;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => wheels.Download(directory.Combine("w"), ["x"]));

        Assert.Contains("python3-pip", error.Message);
        Assert.DoesNotContain(commands, command => command.Arguments.Contains("download"));
    }

    [Fact]
    public void Format_WritesOneLinePerWheelWithItsStatus()
    {
        string text = wheels.Format([
            new() { Name = "a", Version = "1", FileName = "a.whl", License = "MIT", NeedsReview = false },
            new() { Name = "b", Version = "2", FileName = "b.whl", License = "GPL-3.0-only", NeedsReview = true }]);

        Assert.Equal("a\t1\tOK\tMIT\nb\t2\tREVIEW\tGPL-3.0-only\n", text);
    }
}
