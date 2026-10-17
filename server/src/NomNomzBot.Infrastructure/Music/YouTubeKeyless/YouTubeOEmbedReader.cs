// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <inheritdoc />
public sealed class YouTubeOEmbedReader(IHttpClientFactory httpClientFactory) : IYouTubeOEmbedReader
{
    public async Task<YouTubeOEmbed> ReadAsync(string videoId, CancellationToken cancellationToken)
    {
        string watchUrl = YouTubeMusicProvider.WatchUrl(videoId);
        try
        {
            HttpClient client = httpClientFactory.CreateClient(YouTubeInnertube.ClientName);
            using HttpResponseMessage response = await client.GetAsync(
                $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(watchUrl)}&format=json",
                cancellationToken
            );
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(YouTubeOEmbedStatus.NotEmbeddable);
            if (!response.IsSuccessStatusCode)
                return new(YouTubeOEmbedStatus.Unreadable);

            await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken
            );
            using JsonDocument document = await JsonDocument.ParseAsync(
                body,
                cancellationToken: cancellationToken
            );
            JsonElement root = document.RootElement;
            string? title = StringProperty(root, "title");
            return string.IsNullOrWhiteSpace(title)
                ? new(YouTubeOEmbedStatus.Unreadable)
                : new(
                    YouTubeOEmbedStatus.Found,
                    title.Trim(),
                    StringProperty(root, "author_name"),
                    StringProperty(root, "thumbnail_url")
                );
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new(YouTubeOEmbedStatus.Unreadable);
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
