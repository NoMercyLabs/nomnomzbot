// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// Reads <c>youtubei/v1/player</c>. A keyless web call often gets <c>playabilityStatus</c> UNPLAYABLE
/// ("the page needs to be reloaded") while <c>videoDetails</c> is complete, so the verdict comes from
/// <c>videoDetails</c>, not from that status. Only "no details" plus ERROR means the video is missing.
/// </summary>
public sealed class YouTubePlayerDataReader(IHttpClientFactory httpClientFactory)
    : IYouTubePlayerDataReader
{
    public async Task<YouTubePlayerData> ReadAsync(
        string videoId,
        CancellationToken cancellationToken
    )
    {
        HttpClient client = httpClientFactory.CreateClient(YouTubeInnertube.ClientName);
        using JsonDocument? document = await YouTubeInnertube.PostAsync(
            client,
            YouTubeInnertube.PlayerUrl,
            new JsonObject { ["videoId"] = videoId },
            cancellationToken
        );
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            return new(YouTubePlayerDataStatus.Unreadable);

        JsonElement root = document.RootElement;
        string? status = PlayabilityText(root, "status");
        string? reason = PlayabilityText(root, "reason");

        if (!root.TryGetProperty("videoDetails", out JsonElement details))
        {
            if (status == "ERROR")
                return new(YouTubePlayerDataStatus.Missing);
            return IsAgeGate(status, reason)
                ? new(YouTubePlayerDataStatus.AgeRestricted)
                : new(YouTubePlayerDataStatus.Unreadable);
        }

        if (IsAgeGate(status, reason))
            return new(YouTubePlayerDataStatus.AgeRestricted);

        return new(
            YouTubePlayerDataStatus.Read,
            StringOf(details, "title"),
            StringOf(details, "author"),
            LengthSeconds(details),
            BoolOf(details, "isLive") || BoolOf(details, "isUpcoming"),
            PlaysNowhere(root),
            Category(root)
        );
    }

    /// <summary>LOGIN_REQUIRED with an age reason. A "not a bot" sign-in is a bot check, not an age gate.</summary>
    private static bool IsAgeGate(string? status, string? reason) =>
        status == "LOGIN_REQUIRED"
        && reason is not null
        && reason.Contains("age", StringComparison.OrdinalIgnoreCase);

    private static string? PlayabilityText(JsonElement root, string name) =>
        root.TryGetProperty("playabilityStatus", out JsonElement playability)
            ? StringOf(playability, name)
            : null;

    private static int LengthSeconds(JsonElement details) =>
        int.TryParse(
            StringOf(details, "lengthSeconds"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int seconds
        )
            ? seconds
            : 0;

    /// <summary>An <c>availableCountries</c> list that is present and empty: the video plays in no country.</summary>
    private static bool PlaysNowhere(JsonElement root) =>
        root.TryGetProperty("microformat", out JsonElement microformat)
        && microformat.TryGetProperty("playerMicroformatRenderer", out JsonElement renderer)
        && renderer.TryGetProperty("availableCountries", out JsonElement countries)
        && countries.ValueKind == JsonValueKind.Array
        && countries.GetArrayLength() == 0;

    /// <summary>The upload's YouTube category ("Music", "Education", ...); null when the answer carries none.</summary>
    private static string? Category(JsonElement root) =>
        root.TryGetProperty("microformat", out JsonElement microformat)
        && microformat.TryGetProperty("playerMicroformatRenderer", out JsonElement renderer)
            ? StringOf(renderer, "category")
            : null;

    private static string? StringOf(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool BoolOf(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
