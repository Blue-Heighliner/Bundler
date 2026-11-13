namespace BlueHeighliner.Bundler;

/// <summary>
/// Reads the files of a bundle that is in parts as if they were one file: a read-only stream that can seek over the parts.
/// </summary>
internal sealed class PartedReadStream : Stream
{
    private readonly string[] paths;
    private readonly long[] starts;
    private readonly long total;
    private FileStream? current;
    private int index = -1;
    private long position;

    /// <summary>
    /// Opens the parts.
    /// </summary>
    /// <param name="paths">The files, in order.</param>
    public PartedReadStream(IReadOnlyList<string> paths)
    {
        this.paths = [.. paths];
        starts = new long[this.paths.Length];
        foreach (int number in Enumerable.Range(0, this.paths.Length))
        {
            starts[number] = total;
            total += new FileInfo(this.paths[number]).Length;
        }
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => total;

    /// <inheritdoc />
    public override long Position
    {
        get => position;
        set => position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        FileStream? stream = Open();
        if (stream is null || buffer.IsEmpty)
        {
            return 0;
        }

        int read = stream.Read(buffer[..(int)Math.Min(buffer.Length, starts[index] + stream.Length - position)]);
        position += read;
        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        FileStream? stream = Open();
        if (stream is null || buffer.IsEmpty)
        {
            return 0;
        }

        int read = await stream.ReadAsync(buffer[..(int)Math.Min(buffer.Length, starts[index] + stream.Length - position)], cancellationToken);
        position += read;
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => total + offset,
        };
        return position >= 0 ? position : throw new IOException("cannot seek before the start");
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
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

    private FileStream? Open()
    {
        if (position >= total)
        {
            return null;
        }

        int wanted = Array.FindLastIndex(starts, start => start <= position);
        if (wanted != index || current is null)
        {
            current?.Dispose();
            current = new FileStream(paths[wanted], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            index = wanted;
        }

        current.Position = position - starts[index];
        return current;
    }
}
