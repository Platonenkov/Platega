using System.Net;
using System.Text;

namespace Platega.Tests.Infrastructure;

/// <summary>Records outgoing requests and answers with a queued response.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new Queue<(HttpStatusCode, string)>();

    public List<RecordedRequest> Requests { get; } = [];

    public RecordedRequest LastRequest => Requests[^1];

    public StubHttpHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    public StubHttpHandler RespondWithFixture(string fixtureName) => Respond(HttpStatusCode.OK, Fixture.Read(fixtureName));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ContentType?.MediaType,
            body));

        (HttpStatusCode status, string responseBody) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;
}
