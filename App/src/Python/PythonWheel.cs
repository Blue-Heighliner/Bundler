namespace BlueHeighliner.Bundler;

/// <summary>
/// A Python package downloaded as a wheel, with the license it declares.
/// </summary>
internal sealed record PythonWheel
{
    /// <summary>Gets the package name as the wheel states it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the version.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the file name of the wheel.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the license as an SPDX expression where it could be worked out, otherwise as the wheel writes it.</summary>
    public required string License { get; init; }

    /// <summary>Gets a value indicating whether the license is not on the permissive list and has not been reviewed.</summary>
    public required bool NeedsReview { get; init; }
}
