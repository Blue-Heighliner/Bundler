namespace BlueHeighliner.Bundler;

/// <summary>
/// A set of services that is built into one file and installed together.
/// </summary>
internal interface IBundle
{
    /// <summary>Gets the name, which is also the file name stem.</summary>
    string Name { get; }

    /// <summary>Gets a one line description for the list.</summary>
    string Description { get; }

    /// <summary>Gets the system the bundle is made for and has to be installed on, such as <c>Ubuntu 26.04</c>.</summary>
    string Target { get; }

    /// <summary>Gets the position in the list.</summary>
    int Order { get; }

    /// <summary>Gets the bundles that have to be installed first.</summary>
    IReadOnlyList<string> Requires { get; }

    /// <summary>Gets the folders of the staging area that a failed build keeps so the next build continues from them (a half-downloaded model, for example).</summary>
    IReadOnlyList<string> Resumable { get; }

    /// <summary>
    /// Downloads everything the bundle needs, in the latest stable versions, into a folder.
    /// </summary>
    /// <param name="directory">The staging folder.</param>
    /// <param name="cancellation">Stops the build when cancelled.</param>
    /// <returns>What the bundle contains.</returns>
    Task<BuildResult> Build(string directory, CancellationToken cancellation = default);

    /// <summary>
    /// Finds out cheaply, before anything is downloaded, whether the content would be the same as that of the bundle file that is already built.
    /// </summary>
    /// <param name="latest">The checksums of the content of the bundle file that is already built.</param>
    /// <param name="cancellation">Stops the check when cancelled.</param>
    /// <returns><see langword="true" /> when the content is known to be unchanged; <see langword="false" /> when it is changed or the bundle cannot tell without building.</returns>
    Task<bool> IsUnchanged(IReadOnlyDictionary<string, string> latest, CancellationToken cancellation = default);

    /// <summary>
    /// Installs the bundle. Services that are already on the computer are compared with the bundle's: a missing one is
    /// installed, an older one is replaced, the same or a newer one is kept.
    /// </summary>
    /// <param name="context">What the install needs.</param>
    /// <param name="cancellation">Stops the install when cancelled.</param>
    /// <returns>A task that completes when the bundle is installed.</returns>
    Task Install(InstallContext context, CancellationToken cancellation = default);

    /// <summary>
    /// Removes what the bundle installed or replaced itself.
    /// </summary>
    /// <param name="context">What the uninstall needs.</param>
    /// <param name="cancellation">Stops the uninstall when cancelled.</param>
    /// <returns>A task that completes when the bundle is removed.</returns>
    Task Uninstall(UninstallContext context, CancellationToken cancellation = default);
}

/// <summary>
/// The shared part of the bundle definitions.
/// </summary>
internal abstract class BundleBase : IBundle
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public virtual string Target => "Ubuntu 26.04";

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Requires { get; } = [];

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Resumable { get; } = [];

    /// <inheritdoc />
    public abstract Task<BuildResult> Build(string directory, CancellationToken cancellation = default);

    /// <inheritdoc />
    public virtual Task<bool> IsUnchanged(IReadOnlyDictionary<string, string> latest, CancellationToken cancellation = default)
        => Task.FromResult(false);

    /// <inheritdoc />
    public abstract Task Install(InstallContext context, CancellationToken cancellation = default);

    /// <inheritdoc />
    public abstract Task Uninstall(UninstallContext context, CancellationToken cancellation = default);
}
