namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Downloads a cover image from a catalog's image CDN. Timeout and User-Agent are set where it's registered in
/// Program.cs. Throws <see cref="CoverDownloadException"/> on a non-2xx status, timeout or network error.
/// </summary>
public class CoverDownloadClient(HttpClient http)
{
    /// <summary>
    /// The body's bytes, read as a stream and cut off after <see cref="CoverService.MaxCoverBytes"/> + 1 bytes, so a
    /// huge response is never held whole; the caller treats a result that long as too big.
    /// </summary>
    public async Task<byte[]> DownloadAsync(Uri url, CancellationToken ct)
    {
        // HttpClient.Timeout stops at the headers with ResponseHeadersRead, so the body read gets the same limit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(http.Timeout);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new CoverDownloadException($"Cover download failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var limit = CoverService.MaxCoverBytes + 1;
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while (bytes.Length < limit
                   && (read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - bytes.Length)), timeout.Token)) > 0)
                bytes.Write(buffer, 0, read);
            return bytes.ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new CoverDownloadException($"Cover download failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CoverDownloadException("Cover download failed: the request timed out.", ex);
        }
        catch (IOException ex)
        {
            throw new CoverDownloadException($"Cover download failed: {ex.Message}", ex);
        }
    }
}

/// <summary>A cover download failed (bad status, timeout, network error).</summary>
public class CoverDownloadException(string message, Exception? inner = null) : Exception(message, inner);
