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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// Reads <c>youtubei/v1/next</c>, the watch page's data. The video owner there carries the channel's badges;
/// the music-note badge (<c>BADGE_STYLE_TYPE_VERIFIED_ARTIST</c>) is the "Official Artist Channel" mark.
/// The plain check mark (<c>BADGE_STYLE_TYPE_VERIFIED</c>) is any verified channel and does not count.
/// </summary>
public sealed class YouTubeArtistChannelReader(IHttpClientFactory httpClientFactory)
    : IYouTubeArtistChannelReader
{
    private const string ArtistBadgeStyle = "BADGE_STYLE_TYPE_VERIFIED_ARTIST";

    private static readonly string[] OwnerListPath =
    [
        "contents",
        "twoColumnWatchNextResults",
        "results",
        "results",
        "contents",
    ];

    private static readonly string[] OwnerPath =
    [
        "videoSecondaryInfoRenderer",
        "owner",
        "videoOwnerRenderer",
    ];

    public async Task<bool> IsOfficialArtistAsync(
        string videoId,
        CancellationToken cancellationToken
    )
    {
        HttpClient client = httpClientFactory.CreateClient(YouTubeInnertube.ClientName);
        using JsonDocument? document = await YouTubeInnertube.PostAsync(
            client,
            YouTubeInnertube.NextUrl,
            new JsonObject { ["videoId"] = videoId },
            cancellationToken
        );
        if (document is null)
            return false;

        JsonElement? sections = Descend(document.RootElement, OwnerListPath);
        if (sections is not { ValueKind: JsonValueKind.Array } list)
            return false;

        return list.EnumerateArray().Any(HasArtistBadge);
    }

    private static bool HasArtistBadge(JsonElement section)
    {
        JsonElement? owner = Descend(section, OwnerPath);
        if (
            owner is not { } found
            || !found.TryGetProperty("badges", out JsonElement badges)
            || badges.ValueKind != JsonValueKind.Array
        )
            return false;

        return badges.EnumerateArray().Any(IsArtistBadge);
    }

    private static bool IsArtistBadge(JsonElement badge) =>
        badge.ValueKind == JsonValueKind.Object
        && badge.TryGetProperty("metadataBadgeRenderer", out JsonElement renderer)
        && renderer.ValueKind == JsonValueKind.Object
        && renderer.TryGetProperty("style", out JsonElement style)
        && style.ValueKind == JsonValueKind.String
        && style.GetString() == ArtistBadgeStyle;

    private static JsonElement? Descend(JsonElement node, string[] path)
    {
        JsonElement current = node;
        foreach (string name in path)
        {
            if (
                current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(name, out JsonElement next)
            )
                return null;
            current = next;
        }

        return current;
    }
}
