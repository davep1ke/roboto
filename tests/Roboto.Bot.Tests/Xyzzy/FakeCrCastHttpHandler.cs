using System.Net;
using System.Text;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Routes by exact URL to canned JSON, matching CrCastClient's two-request shape (pack
/// info, then cards) without a real network call - same idea as Steam's FakeSteamHttpHandler, but
/// keyed by exact URL rather than substring since crcast's two endpoints for one pack code share a
/// common prefix (".../decks/{code}" and ".../decks/{code}/cards"), which a naive Contains match
/// would confuse.</summary>
public sealed class FakeCrCastHttpHandler : HttpMessageHandler
{
    public Dictionary<string, string> ResponsesByExactUrl { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (!ResponsesByExactUrl.TryGetValue(url, out var json))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
