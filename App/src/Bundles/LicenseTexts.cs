namespace BlueHeighliner.Bundler;

/// <summary>
/// The full texts of the licenses named in the services tables, built into the executable from <c>licenses/texts</c>.
/// </summary>
internal interface ILicenseTexts
{
    /// <summary>
    /// Reads the text of a license.
    /// </summary>
    /// <param name="id">The short name, such as <c>Apache-2.0</c>.</param>
    /// <returns>The text, or a note that there is none when the license has no text file.</returns>
    string Text(string id);
}

/// <inheritdoc />
internal sealed class LicenseTexts(IResources resources) : ILicenseTexts
{
    /// <inheritdoc />
    public string Text(string id)
    {
        try
        {
            return resources.Text($"licenses/{id}.txt");
        }
        catch (FileNotFoundException)
        {
            return $"The text of {id} is not included in this app.";
        }
    }
}
