using System.Net;
using System.Text;

namespace Roboto.Bot.Tests.Steam;

/// <summary>Routes by URL substring to canned JSON, matching real Steam Web API response shapes -
/// lets SteamApiClient's hand-written DTOs be tested against realistic payloads without a real
/// network call.</summary>
public sealed class FakeSteamHttpHandler : HttpMessageHandler
{
    public Dictionary<string, string> ResponsesByUrlContains { get; } = new();
    public List<string> RequestedUrls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        RequestedUrls.Add(url);

        var match = ResponsesByUrlContains.FirstOrDefault(kv => url.Contains(kv.Key, StringComparison.Ordinal));
        if (match.Value is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(match.Value, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
