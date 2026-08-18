using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Roboto.Bot.Steam;

/// <summary>
/// Ports legacy mod_steam_steamapi.cs's three GET calls - HttpClient + System.Text.Json instead of
/// WebClient + Newtonsoft.Json (see MIGRATION.md's "JSON library" note: nothing here needs
/// Newtonsoft's looser JObject-style parsing, these are small fixed shapes).
///
/// Deliberate correction, not a faithful-bug port: GetAchievedCodesAsync filters to Achieved == 1.
/// Legacy's getAchievements() added every achievement name the endpoint returned regardless of
/// that flag, which would have misannounced every achievement in a game as "just earned" the first
/// time any player was ever checked - clearly not the intent, so this fixes it rather than
/// reproducing it.
/// </summary>
public sealed class SteamApiClient(HttpClient http)
{
    private const string BaseUrl = "https://api.steampowered.com";

    public async Task<PlayerSummaryDto?> GetPlayerSummaryAsync(string apiKey, string steamId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(steamId)}";
        var response = await http.GetFromJsonAsync<GetPlayerSummariesResponse>(url, cancellationToken);
        return response?.Response?.Players?.FirstOrDefault();
    }

    public async Task<List<RecentGameDto>> GetRecentlyPlayedGamesAsync(string apiKey, string steamId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/IPlayerService/GetRecentlyPlayedGames/v0001/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}";
        var response = await http.GetFromJsonAsync<GetRecentlyPlayedGamesResponse>(url, cancellationToken);
        return response?.Response?.Games ?? [];
    }

    public async Task<List<string>> GetAchievedCodesAsync(string apiKey, string steamId, string gameId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/ISteamUserStats/GetUserStatsForGame/v0002/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}&appid={Uri.EscapeDataString(gameId)}";
        try
        {
            var response = await http.GetFromJsonAsync<GetUserStatsForGameResponse>(url, cancellationToken);
            return response?.PlayerStats?.Achievements?.Where(a => a.Achieved == 1).Select(a => a.Name).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            // Some games have no stats/achievements at all - Steam 4xx's rather than returning an
            // empty body for those.
            return [];
        }
    }

    public async Task<List<SteamAchievementSchema>> GetGameSchemaAsync(string apiKey, string gameId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(apiKey)}&appid={Uri.EscapeDataString(gameId)}";
        try
        {
            var response = await http.GetFromJsonAsync<GetSchemaForGameResponse>(url, cancellationToken);
            return response?.Game?.AvailableGameStats?.Achievements?
                .Select(a => new SteamAchievementSchema { Code = a.Name ?? "", DisplayName = a.DisplayName ?? "", Description = a.Description ?? "" })
                .ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public sealed class PlayerSummaryDto
    {
        [JsonPropertyName("personaname")]
        public string PersonaName { get; set; } = "";

        [JsonPropertyName("communityvisibilitystate")]
        public int CommunityVisibilityState { get; set; }

        /// <summary>1 = private profile - matches legacy's exact check.</summary>
        public bool IsPrivate => CommunityVisibilityState == 1;
    }

    public sealed class RecentGameDto
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class GetPlayerSummariesResponse
    {
        [JsonPropertyName("response")]
        public PlayerSummariesInner? Response { get; set; }
    }

    private sealed class PlayerSummariesInner
    {
        [JsonPropertyName("players")]
        public List<PlayerSummaryDto>? Players { get; set; }
    }

    private sealed class GetRecentlyPlayedGamesResponse
    {
        [JsonPropertyName("response")]
        public RecentGamesInner? Response { get; set; }
    }

    private sealed class RecentGamesInner
    {
        [JsonPropertyName("games")]
        public List<RecentGameDto>? Games { get; set; }
    }

    private sealed class GetUserStatsForGameResponse
    {
        [JsonPropertyName("playerstats")]
        public PlayerStatsInner? PlayerStats { get; set; }
    }

    private sealed class PlayerStatsInner
    {
        [JsonPropertyName("achievements")]
        public List<AchievementStatDto>? Achievements { get; set; }
    }

    private sealed class AchievementStatDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("achieved")]
        public int Achieved { get; set; }
    }

    private sealed class GetSchemaForGameResponse
    {
        [JsonPropertyName("game")]
        public GameSchemaInner? Game { get; set; }
    }

    private sealed class GameSchemaInner
    {
        [JsonPropertyName("availableGameStats")]
        public AvailableGameStatsDto? AvailableGameStats { get; set; }
    }

    private sealed class AvailableGameStatsDto
    {
        [JsonPropertyName("achievements")]
        public List<AchievementSchemaDto>? Achievements { get; set; }
    }

    private sealed class AchievementSchemaDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
