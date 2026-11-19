namespace BlueHeighliner.Bundler;

/// <summary>
/// The packages for testing programs automatically on virtual displays: Xvfb (a virtual X11 display) with xauth and the Openbox window manager,
/// Cage (a virtual Wayland compositor), the tools that type, click and move windows (xdotool for X11, wtype and wlrctl for Wayland) and the
/// tools that capture the screen (scrot for X11, grim for Wayland, ImageMagick for comparing and converting the pictures). All come from the
/// Ubuntu 24.04 archive.
/// </summary>
internal sealed class VirtualTestingBundle(IAptBuilder apt, IAptInstaller installer, ILog log) : AptProgramBundle(apt, installer, log)
{
    /// <inheritdoc />
    public override string Name => "virtual-testing";

    /// <inheritdoc />
    public override string Description => "Test programs automatically on virtual X11 and Wayland screens: Xvfb, Openbox and Cage, with tools to type, click and take screenshots.";

    /// <inheritdoc />
    public override int Order => 96;

    /// <inheritdoc />
    protected override string Package => "xvfb";

    /// <inheritdoc />
    protected override IReadOnlyList<string> Services { get; } = ["xvfb", "xauth", "xdotool", "scrot", "openbox", "cage", "wtype", "wlrctl", "grim", "imagemagick"];
}
