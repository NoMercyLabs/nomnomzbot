// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Keyless YouTube title lookup over the public oEmbed endpoint. Any failure (bad link, network, timeout,
/// non-200, malformed body) reads as "no title".
/// </summary>
public sealed class YouTubeOEmbedTitleLookup(IHttpClientFactory httpClientFactory)
    : IForeignLinkTitleLookup
{
    public const string ClientName = "youtube-oembed";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public async Task<string?> TryGetTitleAsync(string link, CancellationToken cancellationToken)
    {
        if (!IsYouTubeLink(link))
            return null;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            timeout.CancelAfter(Timeout);

            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using HttpResponseMessage response = await client.GetAsync(
                $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(link)}&format=json",
                timeout.Token
            );
            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            using JsonDocument document = JsonDocument.Parse(body);
            if (
                !document.RootElement.TryGetProperty("title", out JsonElement title)
                || title.ValueKind != JsonValueKind.String
            )
                return null;

            string? text = title.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return null;
        }
    }

    private static bool IsYouTubeLink(string link)
    {
        if (
            !Uri.TryCreate(link, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
            return false;

        string host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be")
            return uri.AbsolutePath.Length > 1;

        if (
            host
            is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        )
            return false;

        return uri.AbsolutePath.StartsWith("/watch", StringComparison.Ordinal)
            || uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal);
    }
}
