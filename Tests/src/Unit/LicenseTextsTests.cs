namespace BlueHeighliner.Bundler.Tests;

public sealed class LicenseTextsTests
{
    [Theory]
    [InlineData("MIT", "Permission is hereby granted")]
    [InlineData("Apache-2.0", "Apache License")]
    [InlineData("GPL-2.0", "GNU GENERAL PUBLIC LICENSE")]
    [InlineData("VSCode-License", "MICROSOFT")]
    public void Text_ReadsTheBuiltInLicense(string id, string expected)
        => Assert.Contains(expected, new LicenseTexts(new Resources()).Text(id), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Text_UnknownLicense_SaysSo()
        => Assert.Contains("not included", new LicenseTexts(new Resources()).Text("Nothing"));
}
