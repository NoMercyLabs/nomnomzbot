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
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.MediaShare.Services;

namespace NomNomzBot.Infrastructure.MediaShare;

/// <summary>
/// <see cref="IMediaSourceResolver"/> (media-share.md D2). Parses a submission URL into a closed-set
/// source and fetches its server-side metadata: Twitch clips via <see cref="ITwitchClipsApi"/> (Get Clips),
/// YouTube videos via the app-level Data API key (videos.list — no per-user OAuth). Never accepts an
/// arbitrary URL. A YouTube video that is a live/upcoming broadcast or not embeddable can't play on the
/// overlay, so it's rejected too.
/// </summary>
public sealed partial class MediaSourceResolver : IMediaSourceResolver
{
    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";

    private readonly ITwitchClipsApi _clips;
    private readonly HttpClient _http;
    private readonly string _youTubeApiKey;
    private readonly ILogger<MediaSourceResolver> _logger;

    public MediaSourceResolver(
        ITwitchClipsApi clips,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MediaSourceResolver> logger
    )
    {
        _clips = clips;
        _http = httpClientFactory.CreateClient("youtube");
        _youTubeApiKey = configuration["YouTube:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<Result<ResolvedMedia>> ResolveAsync(
        string url,
        bool allowTwitchClips,
        bool allowYouTube,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(url))
            return Result.Failure<ResolvedMedia>("A media URL is required.", "VALIDATION_FAILED");

        string trimmed = url.Trim();

        string? clipSlug = ExtractTwitchClipSlug(trimmed);
        if (clipSlug is not null)
        {
            if (!allowTwitchClips)
                return SourceNotAllowed("Twitch clips are not accepted on this channel.");
            return await ResolveTwitchClipAsync(trimmed, clipSlug, ct);
        }

        string? videoId = ExtractYouTubeVideoId(trimmed);
        if (videoId is not null)
        {
            if (!allowYouTube)
                return SourceNotAllowed("YouTube videos are not accepted on this channel.");
            return await ResolveYouTubeAsync(trimmed, videoId, ct);
        }

        return SourceNotAllowed("Only Twitch clips and YouTube videos are accepted.");
    }

    // ─── Twitch clips ──────────────────────────────────────────────────────────

    private async Task<Result<ResolvedMedia>> ResolveTwitchClipAsync(
        string url,
        string slug,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchClip>> lookup = await _clips.GetClipsByIdsAsync([slug], ct);
        if (lookup.IsFailure)
            return Result.Failure<ResolvedMedia>(
                lookup.ErrorMessage ?? "The clip could not be looked up.",
                lookup.ErrorCode
            );

        TwitchClip? clip = lookup.Value.FirstOrDefault();
        if (clip is null)
            return Result.Failure<ResolvedMedia>("That Twitch clip was not found.", "NOT_FOUND");

        return Result.Success(
            new ResolvedMedia(
                Domain.MediaShare.Entities.MediaShareSourceType.TwitchClip,
                slug,
                clip.Title,
                (int)Math.Ceiling(clip.Duration),
                clip.ThumbnailUrl
            )
        );
    }

    // ─── YouTube ────────────────────────────────────────────────────────────────

    private async Task<Result<ResolvedMedia>> ResolveYouTubeAsync(
        string url,
        string videoId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(_youTubeApiKey))
            return Result.Failure<ResolvedMedia>(
                "YouTube submissions are unavailable — the server has no YouTube Data API key.",
                "SERVICE_UNAVAILABLE"
            );

        string requestUrl =
            $"{YouTubeApiBase}/videos?part=snippet,contentDetails,status"
            + $"&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(_youTubeApiKey)}";

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(requestUrl, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<ResolvedMedia>(
                    "YouTube rejected the metadata lookup.",
                    "SERVICE_UNAVAILABLE"
                );

            await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (
                !doc.RootElement.TryGetProperty("items", out JsonElement items)
                || items.GetArrayLength() == 0
            )
                return Result.Failure<ResolvedMedia>(
                    "That YouTube video was not found.",
                    "NOT_FOUND"
                );

            JsonElement item = items[0];
            JsonElement snippet = item.GetProperty("snippet");

            // A live/upcoming broadcast is not an on-demand clip.
            string liveState = snippet.TryGetProperty("liveBroadcastContent", out JsonElement live)
                ? live.GetString() ?? "none"
                : "none";
            if (!string.Equals(liveState, "none", StringComparison.OrdinalIgnoreCase))
                return SourceNotAllowed("Live and upcoming broadcasts can't be queued.");

            // Unembeddable video won't play on the overlay.
            if (
                item.TryGetProperty("status", out JsonElement status)
                && status.TryGetProperty("embeddable", out JsonElement embeddable)
                && embeddable.ValueKind == JsonValueKind.False
            )
                return SourceNotAllowed(
                    "That video can't be embedded, so it can't play on stream."
                );

            string? title = snippet.TryGetProperty("title", out JsonElement titleEl)
                ? titleEl.GetString()
                : null;
            string? thumbnail = ExtractThumbnail(snippet);
            int durationSeconds = ParseIso8601Seconds(
                item.GetProperty("contentDetails").TryGetProperty("duration", out JsonElement dur)
                    ? dur.GetString()
                    : null
            );

            return Result.Success(
                new ResolvedMedia(
                    Domain.MediaShare.Entities.MediaShareSourceType.YouTube,
                    videoId,
                    title,
                    durationSeconds,
                    thumbnail
                )
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "YouTube metadata lookup failed for {VideoId}", videoId);
            return Result.Failure<ResolvedMedia>(
                "YouTube could not be reached for metadata.",
                "SERVICE_UNAVAILABLE"
            );
        }
    }

    private static string? ExtractThumbnail(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out JsonElement thumbs))
            return null;
        foreach (string size in (string[])["medium", "high", "default", "standard"])
        {
            if (
                thumbs.TryGetProperty(size, out JsonElement t)
                && t.TryGetProperty("url", out JsonElement u)
            )
                return u.GetString();
        }
        return null;
    }

    private static int ParseIso8601Seconds(string? iso8601)
    {
        if (string.IsNullOrEmpty(iso8601))
            return 0;
        try
        {
            return (int)Math.Ceiling(XmlConvert.ToTimeSpan(iso8601).TotalSeconds);
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    // ─── URL parsing ─────────────────────────────────────────────────────────────

    private static string? ExtractTwitchClipSlug(string url)
    {
        Match m = TwitchClipPattern().Match(url);
        return m.Success ? m.Groups["slug"].Value : null;
    }

    private static string? ExtractYouTubeVideoId(string url)
    {
        Match m = YouTubePattern().Match(url);
        return m.Success ? m.Groups["id"].Value : null;
    }

    private static Result<ResolvedMedia> SourceNotAllowed(string message) =>
        Result.Failure<ResolvedMedia>(message, "SOURCE_NOT_ALLOWED");

    // clips.twitch.tv/<slug> · (m.|www.)twitch.tv/<channel>/clip/<slug> · (m.|www.)twitch.tv/clip/<slug>
    [GeneratedRegex(
        @"(?:clips\.twitch\.tv/|(?:m\.|www\.)?twitch\.tv/(?:\w+/)?clip/)(?<slug>[A-Za-z0-9_-]+)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex TwitchClipPattern();

    // youtu.be/<id> · youtube.com/watch?v=<id> · youtube.com/shorts/<id> · youtube.com/embed/<id>
    [GeneratedRegex(
        @"(?:youtu\.be/|youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/|live/))(?<id>[A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex YouTubePattern();
}
