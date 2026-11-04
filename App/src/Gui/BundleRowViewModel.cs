namespace BlueHeighliner.Bundler;

/// <summary>
/// One bundle in the window: a row of the list and the details shown for it.
/// </summary>
/// <param name="status">What the row shows.</param>
/// <param name="services">The services the bundle installs.</param>
/// <param name="isSelected">Whether the row starts out ticked.</param>
internal sealed class BundleRowViewModel(BundleStatus status, IReadOnlyList<ServiceRow> services, bool isSelected) : ViewModelBase
{
    private bool selected = isSelected;

    /// <summary>Gets the bundle name.</summary>
    public string Name => status.Name;

    /// <summary>Gets the description.</summary>
    public string Description => status.Description;

    /// <summary>Gets the system the bundle is made for, such as <c>Ubuntu 26.04</c>.</summary>
    public string Target => status.Target;

    /// <summary>Gets the bundles it needs, as text.</summary>
    public string Dependencies => status.Requires.Count == 0 ? "None" : string.Join(", ", status.Requires);

    /// <summary>Gets the services it installs.</summary>
    public IReadOnlyList<ServiceRow> Services { get; } = services;

    /// <summary>Gets the name of the badge kind (<c>Missing</c>, <c>Available</c> or <c>Installed</c>), used to color it.</summary>
    public string Kind => status.State switch
    {
        BundleStanding.NotBuilt => "Missing",
        BundleStanding.Installed => "Installed",
        _ => "Available",
    };

    /// <summary>Gets the text of the badge: <c>missing</c> (no file in the bundles folder), <c>available</c> (a file that is not installed yet) or <c>installed</c>.</summary>
    public string Badge => Kind.ToLowerInvariant();

    /// <summary>Gets when the bundle file in the bundles folder was last changed, as date and time, or a note that there is no file.</summary>
    public string BundleFile => status.FileModified is DateTime time ? time.ToString("yyyy-MM-dd HH:mm") : "None in the bundles folder";

    /// <summary>Gets or sets a value indicating whether the row is ticked.</summary>
    public bool IsSelected
    {
        get => selected;
        set => Set(ref selected, value);
    }
}
