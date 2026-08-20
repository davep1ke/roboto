using System.Net.Http.Json;

namespace Roboto.Bot.Xyzzy;

public sealed record CrCastFetchedCard(string Text, int AnswerCount);

public sealed record CrCastFetchedPack(string Name, string Description, List<CrCastFetchedCard> Questions, List<CrCastFetchedCard> Answers);

/// <summary>Ports legacy's Helpers/cardCast.cs getPackCards - a live HTTP client for the crcast.cc
/// service (a community-run CAH-compatible replacement for the original, now-dead cardcastgame.com
/// API legacy was originally written against). System.Text.Json throughout, not Newtonsoft - the
/// user's explicit ask, and there's no legacy JObject-style loose parsing to match here anyway.</summary>
public sealed class CrCastClient(HttpClient http)
{
    private const string BaseUrl = "https://api.crcast.cc/cc/decks/";

    public async Task<CrCastFetchedPack?> FetchPackAsync(string packCode, CancellationToken cancellationToken)
    {
        try
        {
            var info = await http.GetFromJsonAsync<PackInfoResponse>($"{BaseUrl}{packCode}", cancellationToken);
            var cards = await http.GetFromJsonAsync<CardsResponse>($"{BaseUrl}{packCode}/cards", cancellationToken);
            if (info is null || cards is null)
            {
                return null;
            }

            // Each card's text arrives as chunks meant to be joined around a blank ("__", CAH's own
            // blank placeholder) - chunk-count-minus-one is the number of answers a question needs
            // (a normal single-blank question has 2 chunks -> 1 answer; a "Pick 2" has 3 -> 2).
            var questions = (cards.calls ?? []).Select(JoinChunks).ToList();
            var answers = (cards.responses ?? []).Select(JoinChunks).ToList();

            return new CrCastFetchedPack(info.name ?? packCode, info.description ?? "", questions, answers);
        }
        catch (Exception)
        {
            // Network failure, bad JSON, non-2xx, anything - the caller treats a null result as
            // "couldn't import" uniformly, matching legacy's own single failure path here.
            return null;
        }
    }

    private static CrCastFetchedCard JoinChunks(CardResponse card)
    {
        var chunks = card.text ?? [];
        return new CrCastFetchedCard(string.Join("__", chunks), Math.Max(0, chunks.Count - 1));
    }

    private sealed record PackInfoResponse(string? name, string? description);
    private sealed record CardsResponse(List<CardResponse>? calls, List<CardResponse>? responses);
    private sealed record CardResponse(List<string>? text);
}
