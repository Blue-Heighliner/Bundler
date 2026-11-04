namespace BlueHeighliner.Bundler;

/// <summary>
/// Asks the person in front of the window a question.
/// </summary>
internal interface IDialogs
{
    /// <summary>
    /// Gets or sets the window the dialogs open over.
    /// </summary>
    Window? Owner { get; set; }

    /// <summary>
    /// Asks a yes or no question.
    /// </summary>
    /// <param name="question">The question.</param>
    /// <returns><see langword="true" /> when the answer is yes.</returns>
    Task<bool> Confirm(string question);

    /// <summary>
    /// Shows the full texts of licenses in one window with a tab for each.
    /// </summary>
    /// <param name="title">What the licenses belong to.</param>
    /// <param name="licenses">The short names of the licenses, one tab each.</param>
    /// <param name="selected">The license whose tab is shown first.</param>
    /// <returns>A task that completes when the window is closed.</returns>
    Task ShowLicenses(string title, IReadOnlyList<string> licenses, string selected);
}

/// <inheritdoc />
internal sealed class Dialogs(ILicenseTexts texts) : IDialogs
{
    /// <inheritdoc />
    public Window? Owner { get; set; }

    /// <inheritdoc />
    public async Task<bool> Confirm(string question)
    {
        if (Owner is null)
        {
            return true;
        }

        ConfirmDialog dialog = new() { Question = question };
        return await dialog.ShowDialog<bool>(Owner);
    }

    /// <inheritdoc />
    public async Task ShowLicenses(string title, IReadOnlyList<string> licenses, string selected)
    {
        if (Owner is null)
        {
            return;
        }

        LicenseWindow window = new(title, [.. licenses.Select(license => (license, texts.Text(license)))], selected);
        await window.ShowDialog(Owner);
    }
}
