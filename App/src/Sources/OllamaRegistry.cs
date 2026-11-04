namespace BlueHeighliner.Bundler;

/// <summary>
/// Downloads models from the Ollama registry in Ollama's own on-disk layout (<c>manifests/</c> and <c>blobs/</c>).
/// </summary>
internal interface IOllamaRegistry
{
    /// <summary>
    /// Reads the checksum of a model's manifest. The manifest names every file of the model, so equal checksums mean an
    /// identical model.
    /// </summary>
    /// <param name="name">The model name, such as <c>gemma4</c>.</param>
    /// <param name="tag">The tag, such as <c>26b</c>.</param>
    /// <param name="cancellation">Stops the lookup when cancelled.</param>
    /// <returns>The SHA-256 of the manifest in lower case hexadecimal.</returns>
    Task<string> ManifestHash(string name, string tag, CancellationToken cancellation = default);

    /// <summary>
    /// Downloads a model. Finished files are skipped and partial ones continue with the chunks that are missing. Every file
    /// is checked against the SHA-256 in its name. The manifest, which makes the model visible to Ollama, is written last, and
    /// the model's license text is saved as <c>LICENSE.txt</c> in the folder.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="directory">The folder to write the model into.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>What the model says about itself.</returns>
    Task<OllamaModel> Fetch(string name, string tag, string directory, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class OllamaRegistry(HttpClient client, IHashing hashing, ILog log) : IOllamaRegistry
{
    private static readonly string registry = "https://registry.ollama.ai/v2/library";

    private static readonly int chunkSize = 32 << 20;

    private readonly int connections = 16;

    /// <inheritdoc />
    public async Task<string> ManifestHash(string name, string tag, CancellationToken cancellation = default)
        => Convert.ToHexStringLower(SHA256.HashData(await ManifestBytes(name, tag, cancellation)));

    /// <inheritdoc />
    public async Task<OllamaModel> Fetch(string name, string tag, string directory, CancellationToken cancellation = default)
    {
        byte[] raw = await ManifestBytes(name, tag, cancellation);
        JsonNode manifest = JsonNode.Parse(raw)!;
        List<JsonNode> layers = [manifest["config"]!, .. manifest["layers"]!.AsArray().Select(node => node!)];
        long total = layers.Sum(layer => layer["size"]!.GetValue<long>());
        log.Info($"{name}:{tag}: {layers.Count} files, {total / 1e9:F1} GB");

        string blobs = Path.Combine(directory, "blobs");
        Directory.CreateDirectory(blobs);
        foreach (JsonNode layer in layers)
        {
            await FetchBlob(name, layer["digest"]!.GetValue<string>(), layer["size"]!.GetValue<long>(), blobs, cancellation);
        }

        foreach (JsonNode? layer in manifest["layers"]!.AsArray().Where(layer => layer!["mediaType"]!.GetValue<string>().EndsWith("image.license", StringComparison.Ordinal)))
        {
            File.Copy(BlobPath(blobs, layer!["digest"]!.GetValue<string>()), Path.Combine(directory, "LICENSE.txt"), overwrite: true);
        }

        string manifestPath = Path.Combine(directory, "manifests", "registry.ollama.ai", "library", name, tag);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllBytesAsync(manifestPath + ".partial", raw, cancellation);
        File.Move(manifestPath + ".partial", manifestPath, overwrite: true);

        JsonNode config = JsonNode.Parse(await File.ReadAllTextAsync(BlobPath(blobs, manifest["config"]!["digest"]!.GetValue<string>()), cancellation))!;
        return new()
        {
            Parameters = config["model_type"]?.GetValue<string>() ?? string.Empty,
            Quantization = config["file_type"]?.GetValue<string>() ?? string.Empty,
            Requires = config["requires"]?.GetValue<string>() ?? string.Empty,
            Bytes = total,
        };
    }

    private string BlobPath(string blobs, string digest)
        => Path.Combine(blobs, "sha256-" + digest.Split(':')[1]);

    private async Task<byte[]> ManifestBytes(string name, string tag, CancellationToken cancellation)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{registry}/{name}/manifests/{tag}");
        request.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
        using HttpResponseMessage response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellation);
    }

    private async Task FetchBlob(string name, string digest, long size, string blobs, CancellationToken cancellation)
    {
        string hex = digest.Split(':')[1];
        string final = BlobPath(blobs, digest);
        if (File.Exists(final) && new FileInfo(final).Length == size && await hashing.Sha256(final, cancellation) == hex)
        {
            log.Info($"have {hex[..12]} ({size / 1e6:F0} MB)");
            return;
        }

        string partial = final + ".partial";
        string progressPath = final + ".progress";
        int chunks = (int)((size + chunkSize - 1) / chunkSize);
        HashSet<int> done = [];
        if (File.Exists(partial) && new FileInfo(partial).Length == size && File.Exists(progressPath))
        {
            done = File.ReadAllLines(progressPath).Where(line => int.TryParse(line, out int index) && index < chunks).Select(int.Parse).ToHashSet();
        }
        else
        {
            File.Delete(partial);
            File.Delete(progressPath);
            await using FileStream created = new(partial, FileMode.Create);
            created.SetLength(size);
        }

        long received = done.Sum(index => Math.Min(chunkSize, size - ((long)index * chunkSize)));
        long baseline = received;
        string url = await ResolveUrl(name, digest, cancellation);
        using SemaphoreSlim limit = new(connections);
        object gate = new();
        using SafeHandleScope scope = new(partial);
        await using StreamWriter progress = new(progressPath, append: true) { AutoFlush = true };
        DateTime started = DateTime.UtcNow;

        Task work = Task.WhenAll(Enumerable.Range(0, chunks).Where(index => !done.Contains(index)).Select(async index =>
        {
            await limit.WaitAsync(cancellation);
            try
            {
                long start = (long)index * chunkSize;
                long end = Math.Min(size, start + chunkSize) - 1;
                for (int attempt = 0; ; attempt++)
                {
                    long got = 0;
                    bool expired = false;
                    try
                    {
                        using HttpRequestMessage request = new(HttpMethod.Get, url);
                        request.Headers.Range = new(start, end);
                        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        {
                            expired = response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized;
                            throw new IOException($"the server answered {(int)response.StatusCode} to a range request");
                        }

                        await using Stream body = await response.Content.ReadAsStreamAsync(cancellation);
                        byte[] buffer = new byte[1 << 20];
                        long position = start;
                        int read;
                        while ((read = await body.ReadAsync(buffer, cancellation)) > 0)
                        {
                            RandomAccess.Write(scope.Handle, buffer.AsSpan(0, read), position);
                            position += read;
                            got += read;
                            Interlocked.Add(ref received, read);
                        }

                        if (position != end + 1)
                        {
                            throw new IOException("a chunk ended early");
                        }

                        lock (gate)
                        {
                            progress.WriteLine(index);
                        }

                        return;
                    }
                    catch (Exception exception) when (attempt < 9 && exception is (HttpRequestException or IOException))
                    {
                        Interlocked.Add(ref received, -got);
                        await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellation);
                        if (expired)
                        {
                            url = await ResolveUrl(name, digest, cancellation);
                        }
                    }
                }
            }
            finally
            {
                limit.Release();
            }
        }));

        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5), cancellation));
            long now = Interlocked.Read(ref received);
            double rate = (now - baseline) / Math.Max((DateTime.UtcNow - started).TotalSeconds, 1) / 1e6;
            log.Info($"{hex[..12]} {now * 100.0 / size:F1}% of {size / 1e6:F0} MB, {rate:F1} MB/s");
        }

        await work;
        scope.Dispose();
        if (await hashing.Sha256(partial, cancellation) != hex)
        {
            File.Delete(partial);
            File.Delete(progressPath);
            throw new InvalidOperationException($"file {hex[..12]} failed its checksum, run the build again");
        }

        File.Move(partial, final, overwrite: true);
        File.Delete(progressPath);
    }

    private async Task<string> ResolveUrl(string name, string digest, CancellationToken cancellation)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{registry}/{name}/blobs/{digest}");
        request.Headers.Range = new(0, 0);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        return response.RequestMessage!.RequestUri!.ToString();
    }

    private sealed class SafeHandleScope : IDisposable
    {
        private readonly FileStream stream;

        public SafeHandleScope(string path)
            => stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1, FileOptions.None);

        public Microsoft.Win32.SafeHandles.SafeFileHandle Handle => stream.SafeFileHandle;

        public void Dispose()
            => stream.Dispose();
    }
}
