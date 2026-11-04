namespace BlueHeighliner.Bundler;

/// <summary>
/// A yes or no question. Closes with <see langword="true" /> for yes.
/// </summary>
internal sealed partial class ConfirmDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmDialog" /> class.
    /// </summary>
    public ConfirmDialog()
    {
        InitializeComponent();
        this.FindControl<Button>("Yes")!.Click += (_, _) => Close(true);
        this.FindControl<Button>("No")!.Click += (_, _) => Close(false);
    }

    /// <summary>
    /// Sets the question shown.
    /// </summary>
    public required string Question
    {
        set => this.FindControl<TextBlock>("Message")!.Text = value;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);
}
