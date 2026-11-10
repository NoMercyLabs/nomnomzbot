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
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Community;

/// <summary>
/// Streams a Twitch clip into YouTube's resumable upload (<c>videos.insert</c>, read against the live docs
/// 2026-11-10): a start request with the words and the size answers with the session address in
/// <c>Location</c>, and one PUT sends the bytes there. The clip is never held in memory or on disk. The
/// <c>youtube</c> scope the connect grant already holds is enough. Uploads have their own bucket of 100 calls
/// a day for the whole app, which resets at midnight Pacific time.
/// </summary>
public sealed class YouTubeClipUploadService(
    IHttpClientFactory httpClientFactory,
    IYouTubeAccessTokenProvider tokens,
    ITwitchClipsApi clips,
    ICacheService cache,
    TimeProvider time,
    ILogger<YouTubeClipUploadService> logger
) : IYouTubeClipUploadService
{
    private const string StartUrl =
        "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status";
    private const string VideoType = "video/mp4";
    private const int DailyUploads = 100;
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;
    private const string LimitReason = "uploadLimitExceeded";

    private static readonly TimeZoneInfo GoogleDay = TimeZoneInfo.FindSystemTimeZoneById(
        "America/Los_Angeles"
    );
    private static readonly TimeSpan CountKept = TimeSpan.FromDays(2);

    private readonly HttpClient _http = httpClientFactory.CreateClient("youtube");

    public async Task<Result<YouTubeClipUploadDto>> UploadAsync(
        Guid broadcasterId,
        string clipId,
        YouTubeClipUploadRequest request,
        CancellationToken ct = default
    )
    {
        string privacy = request.Privacy ?? YouTubeVideoPrivacy.Private;
        if (Refusal(request, privacy) is { } refusal)
            return Result.Failure<YouTubeClipUploadDto>(refusal, "VALIDATION_FAILED");

        string? accessToken = await tokens.GetAccessTokenAsync(broadcasterId, ct);
        if (accessToken is null)
            return await OutcomeAsync(YouTubeClipUploadOutcome.NotConnected, ct);
        if (await UploadsLeftTodayAsync(ct) == 0)
            return await OutcomeAsync(YouTubeClipUploadOutcome.LimitReached, ct);

        string? downloadUrl = await DownloadUrlAsync(broadcasterId, clipId, ct);
        if (downloadUrl is null)
            return await OutcomeAsync(YouTubeClipUploadOutcome.ClipUnavailable, ct);

        try
        {
            using HttpResponseMessage clip = await _http.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!clip.IsSuccessStatusCode || clip.Content.Headers.ContentLength is not long size)
                return await OutcomeAsync(YouTubeClipUploadOutcome.ClipUnavailable, ct);

            using HttpResponseMessage start = await StartAsync(
                accessToken,
                request,
                privacy,
                size,
                ct
            );
            if (await StartRefusalAsync(start, ct) is { } outcome)
                return await OutcomeAsync(outcome, ct);
            if (!start.IsSuccessStatusCode || start.Headers.Location is not Uri session)
                return Unavailable(start.StatusCode);

            string? videoId = await SendAsync(session, accessToken, clip, size, ct);
            if (videoId is null)
                return Unavailable(null);

            await CountAsync(ct);
            return Result.Success(
                new YouTubeClipUploadDto(
                    YouTubeClipUploadOutcome.Uploaded,
                    videoId,
                    $"https://www.youtube.com/watch?v={videoId}",
                    await UploadsLeftTodayAsync(ct)
                )
            );
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "YouTube clip upload threw for clip {ClipId}", clipId);
            return Unavailable(null);
        }
    }

    public async Task<int> UploadsLeftTodayAsync(CancellationToken ct = default) =>
        Math.Max(0, DailyUploads - ((await cache.GetAsync<UploadCount>(DayKey(), ct))?.Value ?? 0));

    private static string? Refusal(YouTubeClipUploadRequest request, string privacy)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "A video needs a title.";
        if (request.Title.Length > MaxTitleLength)
            return $"A title has at most {MaxTitleLength} characters.";
        if (request.Description?.Length > MaxDescriptionLength)
            return $"A description has at most {MaxDescriptionLength} characters.";
        return YouTubeVideoPrivacy.All.Contains(privacy)
            ? null
            : "The privacy is private, unlisted or public.";
    }

    /// <summary>The clip's landscape download, asked as the channel that owns the clip; null when Twitch gives none.</summary>
    private async Task<string?> DownloadUrlAsync(
        Guid broadcasterId,
        string clipId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchClip>> found = await clips.GetClipsByIdsAsync([clipId], ct);
        if (found.IsFailure || found.Value.Count == 0)
            return null;

        Result<IReadOnlyList<TwitchClipDownload>> downloads = await clips.GetClipDownloadUrlsAsync(
            broadcasterId,
            found.Value[0].BroadcasterId,
            [clipId],
            ct
        );
        if (downloads.IsFailure)
        {
            logger.LogWarning(
                "Twitch gave no download for clip {ClipId}: {Error} ({Code})",
                clipId,
                downloads.ErrorMessage,
                downloads.ErrorCode
            );
            return null;
        }

        TwitchClipDownload? download = downloads.Value.FirstOrDefault();
        return download?.LandscapeDownloadUrl ?? download?.PortraitDownloadUrl;
    }

    private Task<HttpResponseMessage> StartAsync(
        string accessToken,
        YouTubeClipUploadRequest request,
        string privacy,
        long size,
        CancellationToken ct
    )
    {
        HttpRequestMessage start = new(HttpMethod.Post, StartUrl)
        {
            Content = JsonContent.Create(
                new VideoWords(
                    new(request.Title.Trim(), request.Description ?? string.Empty),
                    new(privacy)
                )
            ),
        };
        start.Headers.Authorization = new("Bearer", accessToken);
        start.Headers.Add("X-Upload-Content-Length", size.ToString(CultureInfo.InvariantCulture));
        start.Headers.Add("X-Upload-Content-Type", VideoType);
        return _http.SendAsync(start, ct);
    }

    /// <summary>The normal state a refused start stands for; null when the start was accepted or is an outage.</summary>
    private static async Task<string?> StartRefusalAsync(
        HttpResponseMessage start,
        CancellationToken ct
    )
    {
        if (start.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return YouTubeClipUploadOutcome.NotAllowed;
        if (start.StatusCode != HttpStatusCode.BadRequest)
            return null;

        string body = await start.Content.ReadAsStringAsync(ct);
        return body.Contains(LimitReason, StringComparison.Ordinal)
            ? YouTubeClipUploadOutcome.LimitReached
            : null;
    }

    /// <summary>Sends the clip's bytes to the session; the new video's id, or null when YouTube did not take them.</summary>
    private async Task<string?> SendAsync(
        Uri session,
        string accessToken,
        HttpResponseMessage clip,
        long size,
        CancellationToken ct
    )
    {
        StreamContent bytes = new(await clip.Content.ReadAsStreamAsync(ct));
        bytes.Headers.ContentType = new MediaTypeHeaderValue(VideoType);
        bytes.Headers.ContentLength = size;
        HttpRequestMessage upload = new(HttpMethod.Put, session) { Content = bytes };
        upload.Headers.Authorization = new("Bearer", accessToken);

        using HttpResponseMessage done = await _http.SendAsync(upload, ct);
        if (!done.IsSuccessStatusCode)
        {
            logger.LogWarning("YouTube refused the clip bytes: {Status}", done.StatusCode);
            return null;
        }

        return (await done.Content.ReadFromJsonAsync<UploadedVideo>(cancellationToken: ct))?.Id;
    }

    private async Task<Result<YouTubeClipUploadDto>> OutcomeAsync(
        string outcome,
        CancellationToken ct
    ) =>
        Result.Success(
            new YouTubeClipUploadDto(outcome, null, null, await UploadsLeftTodayAsync(ct))
        );

    private Result<YouTubeClipUploadDto> Unavailable(HttpStatusCode? status)
    {
        if (status is not null)
            logger.LogWarning("YouTube did not start the upload: {Status}", status);
        return Result.Failure<YouTubeClipUploadDto>(
            "YouTube is temporarily unavailable.",
            "SERVICE_UNAVAILABLE"
        );
    }

    private async Task CountAsync(CancellationToken ct)
    {
        string key = DayKey();
        int used = (await cache.GetAsync<UploadCount>(key, ct))?.Value ?? 0;
        await cache.SetAsync(key, new UploadCount(used + 1), CountKept, ct);
    }

    private string DayKey()
    {
        DateTimeOffset now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), GoogleDay);
        return $"youtube:uploads:{now:yyyy-MM-dd}";
    }

    /// <summary>The uploads counted in one Google day, as a type a cache can hold.</summary>
    private sealed record UploadCount(int Value);

    private sealed record VideoWords(
        [property: JsonPropertyName("snippet")] VideoSnippet Snippet,
        [property: JsonPropertyName("status")] VideoStatus Status
    );

    private sealed record VideoSnippet(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description
    );

    private sealed record VideoStatus(
        [property: JsonPropertyName("privacyStatus")] string PrivacyStatus
    );

    private sealed class UploadedVideo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
