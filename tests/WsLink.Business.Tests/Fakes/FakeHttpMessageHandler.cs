using System.Collections.Concurrent;
using System.Net;

namespace WsLink.Business.Tests.Fakes;

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    string? ContentType,
    string? Content);

/// <summary>
/// Records sent requests and answers them by the given callback, no real network is used.
/// </summary>
public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedRequest> requests = new();

    public IReadOnlyList<RecordedRequest> Requests => requests.ToList();

    public static FakeHttpMessageHandler Returning(HttpStatusCode statusCode) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

    public static FakeHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Content is read here, the adapters dispose the request right after sending.
        var content = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!,
            request.Headers.Authorization?.ToString(), request.Content?.Headers.ContentType?.MediaType, content));

        return await respond(request, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
