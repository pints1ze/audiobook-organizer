using System.Net;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class CoverDownloadClientTests
{
    private static readonly Uri CoverUrl = new("https://m.media-amazon.com/images/I/test.jpg");

    private static CoverDownloadClient Client(HttpMessageHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task Returns_the_bytes_on_200_from_the_requested_url()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var result = await Client(handler).DownloadAsync(CoverUrl, CancellationToken.None);

        Assert.Equal(bytes, result);
        Assert.Equal(CoverUrl, Assert.Single(handler.Requests).RequestUri);
    }

    [Fact]
    public async Task Throws_on_a_404()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<CoverDownloadException>(() => Client(handler).DownloadAsync(CoverUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_on_a_timeout_or_network_error()
    {
        var timeout = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));
        var network = new FakeHttpMessageHandler(_ => throw new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<CoverDownloadException>(() => Client(timeout).DownloadAsync(CoverUrl, CancellationToken.None));
        await Assert.ThrowsAsync<CoverDownloadException>(() => Client(network).DownloadAsync(CoverUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Stops_reading_a_response_over_10_MB_after_one_byte_too_many()
    {
        var body = new EndlessStream();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });

        var result = await Client(handler).DownloadAsync(CoverUrl, CancellationToken.None);

        Assert.Equal(CoverService.MaxCoverBytes + 1, result.Length);
        // Read in buffer-sized chunks, so at most one buffer past the limit.
        Assert.True(body.BytesRead < CoverService.MaxCoverBytes + 1024 * 1024, $"read {body.BytesRead} bytes");
    }

    /// <summary>A body that never ends, counting what's read from it.</summary>
    private class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)0xAB, offset, count);
            BytesRead += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
