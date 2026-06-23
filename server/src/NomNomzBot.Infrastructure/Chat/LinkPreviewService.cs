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
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Resolves an OpenGraph link preview (chat-decoration spec §3.5). It serves a cached preview when present, otherwise
/// fetches the page through the SSRF-hardened <c>egress-allowlisted</c> client (resolve-then-pin, no redirects, internal
/// IPs blocked), reads a bounded amount of HTML, scrapes the og:title/description/image meta tags, and caches the result.
/// Best-effort: a non-html response, a blocked/failed fetch, or a page with no OpenGraph tags returns a null preview.
/// </summary>
public sealed class LinkPreviewService : ILinkPreviewService
{
    private const int MaxHtmlBytes = 512 * 1024;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<LinkPreviewService> _logger;

    public LinkPreviewService(
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        ILogger<LinkPreviewService> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<LinkPreview?>> FetchAsync(Uri url, CancellationToken ct = default)
    {
        string cacheKey = $"chat:linkpreview:{url.Scheme}://{url.Authority}{url.PathAndQuery}";
        LinkPreview? cached = await _cache.GetAsync<LinkPreview>(cacheKey, ct);
        if (cached is not null)
            return Result.Success<LinkPreview?>(cached);

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(EgressHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!response.IsSuccessStatusCode)
                return Result.Success<LinkPreview?>(null);

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("text/html" or "application/xhtml+xml"))
                return Result.Success<LinkPreview?>(null);

            string html = await ReadCappedAsync(response, ct);
            LinkPreview preview = new(
                url.Host,
                ExtractOpenGraph(html, "title"),
                ExtractOpenGraph(html, "description"),
                ExtractOpenGraph(html, "image")
            );

            await _cache.SetAsync(cacheKey, preview, CacheTtl, ct);
            return Result.Success<LinkPreview?>(preview);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // SSRF block, DNS failure, timeout, or a truncated body — degrade to no preview.
            _logger.LogDebug(ex, "Link preview fetch failed for {Host}.", url.Host);
            return Result.Success<LinkPreview?>(null);
        }
    }

    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
        byte[] buffer = new byte[MaxHtmlBytes];
        int total = 0;
        int read;
        while (
            total < MaxHtmlBytes
            && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxHtmlBytes - total), ct)) > 0
        )
            total += read;

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    // Scrapes one OpenGraph meta tag, tolerating either attribute order (property/name before or after content).
    private static string? ExtractOpenGraph(string html, string property)
    {
        Match forward = Regex.Match(
            html,
            $"<meta[^>]+?(?:property|name)\\s*=\\s*[\"']og:{property}[\"'][^>]+?content\\s*=\\s*[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase
        );
        if (forward.Success)
            return Decode(forward.Groups[1].Value);

        Match backward = Regex.Match(
            html,
            $"<meta[^>]+?content\\s*=\\s*[\"']([^\"']*)[\"'][^>]+?(?:property|name)\\s*=\\s*[\"']og:{property}[\"']",
            RegexOptions.IgnoreCase
        );
        return backward.Success ? Decode(backward.Groups[1].Value) : null;
    }

    private static string? Decode(string value) =>
        string.IsNullOrEmpty(value) ? null : WebUtility.HtmlDecode(value);
}
