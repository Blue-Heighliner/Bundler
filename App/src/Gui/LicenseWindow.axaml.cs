namespace BlueHeighliner.Bundler;

/// <summary>
/// A window with a read-only text viewer for each of some licenses, in tabs.
/// </summary>
internal sealed partial class LicenseWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseWindow" /> class.
    /// </summary>
    /// <param name="title">What the licenses belong to, shown in the title.</param>
    /// <param name="licenses">The short name and the full text of each license.</param>
    /// <param name="selected">The short name of the license whose tab is shown first.</param>
    public LicenseWindow(string title, IReadOnlyList<(string Id, string Text)> licenses, string selected)
    {
        InitializeComponent();
        Title = $"{title}: license";
        TabControl tabs = this.FindControl<TabControl>("Tabs")!;
        foreach ((string id, string text) in licenses)
        {
            tabs.Items.Add(new TabItem
            {
                Header = id,
                Content = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
                    FontSize = 12.5,
                    BorderThickness = new(0),
                    CornerRadius = new(0),
                },
            });
        }

        tabs.SelectedIndex = Math.Max(0, licenses.ToList().FindIndex(license => license.Id == selected));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseWindow" /> class for the XAML previewer.
    /// </summary>
    public LicenseWindow()
        => throw new NotSupportedException("the window needs its licenses");

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);
}
