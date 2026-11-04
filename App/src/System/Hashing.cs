namespace BlueHeighliner.Bundler;

/// <summary>
/// Computes and checks file checksums.
/// </summary>
internal interface IHashing
{
    /// <summary>
    /// Computes the SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns>The hash in lower case hexadecimal.</returns>
    Task<string> Sha256(string path, CancellationToken cancellation = default);

    /// <summary>
    /// Checks a file against an expected checksum.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="expected">The checksum, or <see langword="null" /> when nothing is to be checked.</param>
    /// <param name="cancellation">Stops the work when cancelled.</param>
    /// <returns><see langword="true" /> when the file matches or there is nothing to check.</returns>
    Task<bool> Matches(string path, HashSpec? expected, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class Hashing : IHashing
{
    /// <inheritdoc />
    public async Task<string> Sha256(string path, CancellationToken cancellation = default)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation));
    }

    /// <inheritdoc />
    public async Task<bool> Matches(string path, HashSpec? expected, CancellationToken cancellation = default)
    {
        if (expected is null)
        {
            return true;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        byte[] hash = expected.Algorithm == "sha512"
            ? await SHA512.HashDataAsync(stream, cancellation)
            : await SHA256.HashDataAsync(stream, cancellation);
        return Convert.ToHexStringLower(hash) == expected.Hex;
    }
}
