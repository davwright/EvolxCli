using System.Net;

namespace Evolx.Cli.Tests.Http;

/// <summary>
/// Deterministic HTTP handler for gateway tests. Lets each test queue the responses it
/// expects in order, and records every request so the test can assert on what was sent.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpHandler EnqueueStatus(HttpStatusCode status, string? body = null,
        Action<HttpResponseMessage>? mutate = null)
    {
        _responders.Enqueue(req =>
        {
            var resp = new HttpResponseMessage(status);
            if (body != null) resp.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            resp.RequestMessage = req;
            mutate?.Invoke(resp);
            return resp;
        });
        return this;
    }

    public FakeHttpHandler EnqueueException(Exception ex)
    {
        _responders.Enqueue(_ => throw ex);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the request body so the test can assert on it (clones since requests aren't reusable)
        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            foreach (var h in request.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            Requests.Add(clone);
        }
        else
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var h in request.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            Requests.Add(clone);
        }

        if (_responders.Count == 0)
            throw new InvalidOperationException("FakeHttpHandler ran out of queued responders.");
        return _responders.Dequeue()(request);
    }
}
