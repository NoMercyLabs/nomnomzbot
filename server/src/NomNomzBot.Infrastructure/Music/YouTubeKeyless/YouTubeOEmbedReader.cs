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
