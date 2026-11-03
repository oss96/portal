using System.Net;

namespace Portal.Tests.Updates;

/// <summary>Answers requests from a URL-to-response table and records every request it sees.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> routes = new(StringComparer.Ordinal);

    /// <summary>Requests in the order they arrived.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Answers <paramref name="url"/> with a JSON body.</summary>
    public FakeHttpHandler Json(string url, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Route(url, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <summary>Answers <paramref name="url"/> with raw bytes.</summary>
    public FakeHttpHandler Bytes(string url, byte[] body) =>
        Route(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    /// <summary>Answers <paramref name="url"/> with an empty response of the given status.</summary>
    public FakeHttpHandler Status(string url, HttpStatusCode status) =>
        Route(url, () => new HttpResponseMessage(status));

    /// <summary>Answers <paramref name="url"/> by calling <paramref name="respond"/>.</summary>
    public FakeHttpHandler Route(string url, Func<HttpResponseMessage> respond)
    {
        routes[url] = respond;
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var url = request.RequestUri!.ToString();
        if (!routes.TryGetValue(url, out var respond))
        {
            throw new HttpRequestException($"no route for {url}");
        }

        return Task.FromResult(respond());
    }
}
