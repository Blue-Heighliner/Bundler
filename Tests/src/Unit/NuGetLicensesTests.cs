using System.IO.Compression;

namespace BlueHeighliner.Bundler.Tests;

public sealed class NuGetLicensesTests
{
    private readonly Mock<IResources> resources = new();
    private readonly NuGetLicenses licenses;

    public NuGetLicensesTests()
    {
        resources.Setup(r => r.Text("reviewed-nuget.tsv")).Returns("# id\tlicense\nReviewed.Package\tCustom terms read by a person\n");
        licenses = new(resources.Object);
    }

    private static void Package(string folder, string id, string licenseElement)
    {
        Directory.CreateDirectory(folder);
        using ZipArchive zip = ZipFile.Open(Path.Combine(folder, $"{id}.1.0.0.nupkg"), ZipArchiveMode.Create);
        using StreamWriter writer = new(zip.CreateEntry($"{id}.nuspec").Open());
        writer.Write($"<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>{id}</id><version>1.0.0</version>{licenseElement}</metadata></package>");
    }

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("MIT OR Apache-2.0", true)]
    [InlineData("(MIT AND BSD-3-Clause)", true)]
    [InlineData("Apache-2.0 WITH LLVM-exception", true)]
    [InlineData("GPL-3.0-only", false)]
    [InlineData("MIT OR SSPL-1.0", false)]
    [InlineData("", false)]
    public void IsPermissive_JudgesTheExpression(string expression, bool expected)
        => Assert.Equal(expected, licenses.IsPermissive(expression));

    [Fact]
    public void Report_ClassifiesPackages()
    {
        using TempDirectory directory = new();
        Package(directory.Path, "Good.Package", "<license type=\"expression\">MIT</license>");
        Package(directory.Path, "Reviewed.Package", "<license type=\"file\">LICENSE.txt</license>");
        Package(directory.Path, "Bad.Package", "<licenseUrl>https://example.com/license</licenseUrl>");
        Package(directory.Path, "Gpl.Package", "<license type=\"expression\">GPL-3.0-only</license>");

        IReadOnlyList<LicenseRow> rows = licenses.Report(directory.Path);

        Assert.Equal("OK", rows.Single(row => row.Id == "Good.Package").Status);
        Assert.Equal("OK", rows.Single(row => row.Id == "Reviewed.Package").Status);
        Assert.Contains("(reviewed)", rows.Single(row => row.Id == "Reviewed.Package").License);
        Assert.Equal("REVIEW", rows.Single(row => row.Id == "Bad.Package").Status);
        Assert.Equal("REVIEW", rows.Single(row => row.Id == "Gpl.Package").Status);
        Assert.Equal(["Bad.Package", "Good.Package", "Gpl.Package", "Reviewed.Package"], rows.Select(row => row.Id));
    }

    [Fact]
    public void Report_UnreadablePackage_NeedsReview()
    {
        using TempDirectory directory = new();
        directory.Write("broken.1.0.nupkg", "not a zip");

        Assert.Equal("REVIEW", Assert.Single(licenses.Report(directory.Path)).Status);
    }

    [Fact]
    public void Format_WritesTabSeparatedRows()
        => Assert.Equal("A\t1\tOK\tMIT\n", licenses.Format([new("A", "1", "OK", "MIT")]));
}
