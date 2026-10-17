// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// YouTube's public web endpoints (<c>youtubei/v1</c>), the ones youtube.com itself calls. They need no
/// API key and no login, and they never spend the bot owner's Data API quota.
/// </summary>
internal static class YouTubeInnertube
{
    public const string ClientName = "youtube-web";

    public const string PlayerUrl = "https://www.youtube.com/youtubei/v1/player?prettyPrint=false";
    public const string SearchUrl = "https://www.youtube.com/youtubei/v1/search?prettyPrint=false";

    private const string WebClientVersion = "2.20251001.00.00";

    /// <summary>POSTs <paramref name="fields"/> with the web client context; null on any failure.</summary>
    public static async Task<JsonDocument?> PostAsync(
        HttpClient client,
        string url,
        JsonObject fields,
        CancellationToken cancellationToken
    )
    {
        fields["context"] = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["clientName"] = "WEB",
                ["clientVersion"] = WebClientVersion,
                ["hl"] = "en",
                ["gl"] = "US",
            },
        };

        try
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                url,
                fields,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
                return null;

            await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken
            );
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The text of a <c>{"simpleText":..}</c> or <c>{"runs":[{"text":..}]}</c> node; null when absent.</summary>
    public static string? Text(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return null;
        if (node.TryGetProperty("simpleText", out JsonElement simple))
            return simple.GetString();
        if (
            !node.TryGetProperty("runs", out JsonElement runs)
            || runs.ValueKind != JsonValueKind.Array
        )
            return null;
        return string.Concat(runs.EnumerateArray().Select(RunText));
    }

    private static string? RunText(JsonElement run) =>
        run.TryGetProperty("text", out JsonElement text) ? text.GetString() : null;
}
