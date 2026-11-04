namespace BlueHeighliner.Bundler;

/// <summary>
/// Downloads from the internet.
/// </summary>
internal interface IHttpFetcher
{
    /// <summary>
    /// Downloads a text.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="bearerToken">An access token to send, or <see langword="null" />.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The text.</returns>
    Task<string> GetString(string url, string? bearerToken = null, CancellationToken cancellation = default);

    /// <summary>
    /// Downloads a JSON document.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="bearerToken">An access token to send, or <see langword="null" />.</param>
    /// <param name="cancellation">Stops the download when cancelled.</param>
    /// <returns>The parsed document.</returns>
    Task<JsonNode> GetJson(string url, string? bearerToken = null, CancellationToken cancellation = default);

    /// <summary>
    /// Sends a JSON document and reads the JSON answer.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="body">The document to send.</param>
    /// <param name="cancellation">Stops the request when cancelled.</param>
    /// <returns>The parsed answer.</returns>
    Task<JsonNode> PostJson(string url, JsonNode body, CancellationToken cancellation = default);

    /// <summary>
    /// Downloads a file, continuing a partial download and checking the checksum.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="destination">Where to put the file. A complete file that matches the checksum is kept as it is.</param>
    /// <param name="expected">The checksum the file must have, or <see langword="null" /> to accept it unchecked.</param>
    /// <param name="cancellation">Stops the download when cancelled (the partial file stays for the next try).</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="InvalidOperationException">The download failed or its checksum is wrong.</exception>
    Task Download(string url, string destination, HashSpec? expected = null, CancellationToken cancellation = default);
}

/// <inheritdoc />
internal sealed class HttpFetcher(HttpClient client, IHashing hashing, ILog log) : IHttpFetcher
{
    /// <inheritdoc />
    public async Task<string> GetString(string url, string? bearerToken = null, CancellationToken cancellation = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new("Bearer", bearerToken);
        }

        using HttpResponseMessage response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellation);
    }

    /// <inheritdoc />
    public async Task<JsonNode> GetJson(string url, string? bearerToken = null, CancellationToken cancellation = default)
        => JsonNode.Parse(await GetString(url, bearerToken, cancellation)) ?? throw new InvalidOperationException($"{url} did not return JSON");

    /// <inheritdoc />
    public async Task<JsonNode> PostJson(string url, JsonNode body, CancellationToken cancellation = default)
    {
        using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.ParseAdd("application/json;api-version=7.1-preview.1");
        using HttpResponseMessage response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) ?? throw new InvalidOperationException($"{url} did not return JSON");
    }

    /// <inheritdoc />
    public async Task Download(string url, string destination, HashSpec? expected = null, CancellationToken cancellation = default)
    {
        if (File.Exists(destination) && await hashing.Matches(destination, expected, cancellation))
        {
            log.Info($"have {Path.GetFileName(destination)}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".partial";
        if (expected is not null && File.Exists(partial) && await hashing.Matches(partial, expected, cancellation))
        {
            File.Move(partial, destination, overwrite: true);
            return;
        }

        log.Info($"downloading {Path.GetFileName(destination)}");
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await Transfer(url, partial, cancellation);
                break;
            }
            catch (Exception exception) when (attempt < 5 && exception is (HttpRequestException or IOException))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            }
        }

        if (!await hashing.Matches(partial, expected, cancellation))
        {
            File.Delete(partial);
            throw new InvalidOperationException($"checksum mismatch for {url} (the partial file was removed, run the build again)");
        }

        File.Move(partial, destination, overwrite: true);
    }

    private async Task Transfer(string url, string partial, CancellationToken cancellation)
    {
        long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (have > 0)
        {
            request.Headers.Range = new(have, null);
        }

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        bool resumed = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        await using FileStream file = new(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellation);
        await body.CopyToAsync(file, 1 << 20, cancellation);
    }
}
