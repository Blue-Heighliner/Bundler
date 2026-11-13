namespace BlueHeighliner.Bundler;

/// <summary>
/// Writes what it is given into files that are at most a size limit each: a write-only stream that starts the next file when the
/// current one is full. The next file is started only when there is more to write, so there is never an empty one.
/// </summary>
/// <param name="pathOf">Gives the path of a file by its number, from 1.</param>
/// <param name="limit">The most bytes one file may hold, or <see langword="null" /> for one file of any size.</param>
internal sealed class PartedWriteStream(Func<int, string> pathOf, long? limit) : Stream
{
    private readonly List<string> paths = [];
    private FileStream? current;
    private long inCurrent;
    private long total;

    /// <summary>Gets the paths of the files written so far, in order.</summary>
    public IReadOnlyList<string> Paths => paths;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => total;

    /// <inheritdoc />
    public override long Position
    {
        get => total;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            FileStream stream = Room();
            int count = limit is long maximum ? (int)Math.Min(buffer.Length, maximum - inCurrent) : buffer.Length;
            stream.Write(buffer[..count]);
            Advance(count);
            buffer = buffer[count..];
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            FileStream stream = Room();
            int count = limit is long maximum ? (int)Math.Min(buffer.Length, maximum - inCurrent) : buffer.Length;
            await stream.WriteAsync(buffer[..count], cancellationToken);
            Advance(count);
            buffer = buffer[count..];
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush()
        => current?.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
        => current?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            current?.Dispose();
            current = null;
        }

        base.Dispose(disposing);
    }

    private FileStream Room()
    {
        if (current is not null && (limit is not long maximum || inCurrent < maximum))
        {
            return current;
        }

        current?.Dispose();
        string path = pathOf(paths.Count + 1);
        paths.Add(path);
        inCurrent = 0;
        current = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        return current;
    }

    private void Advance(int count)
    {
        inCurrent += count;
        total += count;
    }
}
