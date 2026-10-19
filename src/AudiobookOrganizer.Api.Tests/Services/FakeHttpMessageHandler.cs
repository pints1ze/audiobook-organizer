using System.Net;
using System.Text;

namespace AudiobookOrganizer.Api.Tests.Services;

/// <summary>Answers every request from <paramref name="respond"/> and records the requests, so no test reaches the network.</summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static FakeHttpMessageHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    public static FakeHttpMessageHandler Fixture(string name) =>
        Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "audible", name)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
