using System.IO.Compression;

namespace BlueHeighliner.Bundler.Tests;

public sealed class TemplatePatcherTests
{
    private readonly TemplatePatcher patcher = new();

    private static string Pack(TempDirectory directory, Dictionary<string, string> entries)
    {
        string path = directory.Combine("templates.nupkg");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            using StreamWriter writer = new(zip.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }

    private static string Read(string nupkg, string name)
    {
        using ZipArchive zip = ZipFile.OpenRead(nupkg);
        using StreamReader reader = new(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Patch_RemovesReferencesAndCalls()
    {
        using TempDirectory directory = new();
        string nupkg = Pack(
            directory,
            new()
            {
                ["templates.nuspec"] = "<package><metadata><description>Templates</description></metadata></package>",
                ["app/App.csproj"] = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Avalonia.Diagnostics\" Condition=\"x\" />\n    <PackageReference Include=\"Avalonia\" />\n  </ItemGroup>\n</Project>",
                ["app/Program.cs"] = "builder\n    .UseDiag()\n    .Run();\n#if DEBUG\n    .WithDiag()\n#endif\n",
            });

        IReadOnlyDictionary<string, int> removed = patcher.Patch(nupkg, ["Avalonia.Diagnostics"], ["UseDiag"]);

        Assert.Equal(1, removed["Avalonia.Diagnostics"]);
        Assert.Equal(1, removed["UseDiag()"]);
        Assert.DoesNotContain("Avalonia.Diagnostics", Read(nupkg, "app/App.csproj"));
        Assert.Contains("Include=\"Avalonia\"", Read(nupkg, "app/App.csproj"));
        Assert.DoesNotContain("UseDiag", Read(nupkg, "app/Program.cs"));
        Assert.Contains("Modified by bundler", Read(nupkg, "templates.nuspec"));
    }

    [Fact]
    public void Patch_NothingToRemove_ThrowsBecauseThePackChanged()
    {
        using TempDirectory directory = new();
        string nupkg = Pack(directory, new() { ["app/App.csproj"] = "<Project />" });

        Assert.Throws<InvalidOperationException>(() => patcher.Patch(nupkg, ["Gone.Package"], []));
    }

    [Fact]
    public void Patch_DropsSignatureFiles()
    {
        using TempDirectory directory = new();
        string nupkg = Pack(directory, new() { ["app/App.csproj"] = "<Project><PackageReference Include=\"X\" /></Project>", [".signature.p7s"] = "sig" });

        patcher.Patch(nupkg, ["X"], []);

        using ZipArchive zip = ZipFile.OpenRead(nupkg);
        Assert.Null(zip.GetEntry(".signature.p7s"));
    }
}
