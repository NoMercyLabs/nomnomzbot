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
/// Reads <c>youtubei/v1/search</c> filtered to videos. Every <c>videoRenderer</c> in the answer is a hit,
/// wherever YouTube nests it; one without a <c>lengthText</c> is a live stream or premiere and is left out.
/// </summary>
public sealed class YouTubeWebSearch(IHttpClientFactory httpClientFactory) : IYouTubeWebSearch
{
    /// <summary>The search filter "Type: Video" as YouTube's web page sends it.</summary>
    private const string VideosOnlyFilter = "EgIQAQ%3D%3D";

    public async Task<IReadOnlyList<YouTubeWebSearchHit>?> SearchAsync(
        string query,
        CancellationToken cancellationToken
    )
    {
        HttpClient client = httpClientFactory.CreateClient(YouTubeInnertube.ClientName);
        using JsonDocument? document = await YouTubeInnertube.PostAsync(
            client,
            YouTubeInnertube.SearchUrl,
            new JsonObject { ["query"] = query, ["params"] = VideosOnlyFilter },
            cancellationToken
        );
        if (document is null)
            return null;

        List<YouTubeWebSearchHit> hits = [];
        foreach (JsonElement renderer in VideoRenderers(document.RootElement))
        {
            YouTubeWebSearchHit? hit = ToHit(renderer);
            if (hit is not null && hits.All(seen => seen.VideoId != hit.VideoId))
                hits.Add(hit);
        }
        return hits;
    }

    private static IEnumerable<JsonElement> VideoRenderers(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.NameEquals("videoRenderer"))
                    yield return property.Value;
                else
                    foreach (JsonElement nested in VideoRenderers(property.Value))
                        yield return nested;
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            foreach (JsonElement nested in VideoRenderers(item))
                yield return nested;
        }
    }

    private static YouTubeWebSearchHit? ToHit(JsonElement renderer)
    {
        if (
            !renderer.TryGetProperty("videoId", out JsonElement id)
            || id.GetString() is not { Length: 11 } videoId
            || !renderer.TryGetProperty("lengthText", out JsonElement lengthText)
        )
            return null;

        int durationMs = ParseClockMs(YouTubeInnertube.Text(lengthText));
        string? title = renderer.TryGetProperty("title", out JsonElement t)
            ? YouTubeInnertube.Text(t)
            : null;
        if (durationMs <= 0 || string.IsNullOrWhiteSpace(title))
            return null;

        string channel = renderer.TryGetProperty("ownerText", out JsonElement owner)
            ? YouTubeInnertube.Text(owner) ?? string.Empty
            : string.Empty;
        return new(videoId, title, channel, durationMs);
    }

    /// <summary>"3:34" or "1:02:03" to milliseconds; 0 when unreadable.</summary>
    internal static int ParseClockMs(string? clock)
    {
        if (string.IsNullOrWhiteSpace(clock))
            return 0;

        long seconds = 0;
        foreach (string part in clock.Trim().Split(':'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return 0;
            seconds = (seconds * 60) + value;
        }
        return (int)Math.Min(seconds * 1000, int.MaxValue);
    }
}
