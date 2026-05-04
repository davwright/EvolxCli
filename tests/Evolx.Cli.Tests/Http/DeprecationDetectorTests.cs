using System.Net;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Http;

public class DeprecationDetectorTests
{
    /// <summary>
    /// Capture stderr produced by code under test so we can assert on the warning lines.
    /// Restores Console.Error on dispose.
    /// </summary>
    private sealed class StderrCapture : IDisposable
    {
        private readonly TextWriter _previous;
        private readonly StringWriter _writer = new();
        public StderrCapture()
        {
            _previous = Console.Error;
            Console.Error.Flush();
            Console.SetError(_writer);
        }
        public string Text => _writer.ToString();
        public void Dispose() => Console.SetError(_previous);
    }

    private static HttpResponseMessage MakeResp(string method, string url, Action<HttpResponseMessage>? mutate = null)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(new HttpMethod(method), url),
        };
        mutate?.Invoke(resp);
        return resp;
    }

    [Fact]
    public void No_deprecation_headers_means_no_output()
    {
        using var cap = new StderrCapture();
        DeprecationDetector.Inspect(MakeResp("GET", "https://x/clean"));
        cap.Text.Should().BeEmpty();
    }

    [Fact]
    public void Deprecation_header_emits_one_warning_line()
    {
        using var cap = new StderrCapture();
        var resp = MakeResp("GET", "https://x/old", r =>
            r.Headers.TryAddWithoutValidation("Deprecation", "Sun, 11 Nov 2025 23:59:59 GMT"));

        DeprecationDetector.Inspect(resp);

        cap.Text.Should().Contain("DEPRECATED");
        cap.Text.Should().Contain("GET /old");
        cap.Text.Should().Contain("2025");
    }

    [Fact]
    public void Sunset_header_appended_when_present()
    {
        using var cap = new StderrCapture();
        var resp = MakeResp("GET", "https://x/old2", r =>
        {
            r.Headers.TryAddWithoutValidation("Deprecation", "true");
            r.Headers.TryAddWithoutValidation("Sunset", "Wed, 11 Nov 2026 23:59:59 GMT");
        });

        DeprecationDetector.Inspect(resp);

        cap.Text.Should().Contain("removed by");
        cap.Text.Should().Contain("2026");
    }
}
