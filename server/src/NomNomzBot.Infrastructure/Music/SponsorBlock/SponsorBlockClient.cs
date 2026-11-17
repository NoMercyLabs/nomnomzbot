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

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NomNomzBot.Infrastructure.Music.SponsorBlock;

/// <summary>Reads the community off-topic marks of a video from SponsorBlock.</summary>
public interface ISponsorBlockClient
{
    /// <summary>
    /// The music section of the video, or null when the whole video plays: no marks, an unreachable service
    /// or an unreadable answer all mean the video plays as it is.
    /// </summary>
    Task<YouTubeMusicSection?> ReadMusicSectionAsync(
        string videoId,
        int knownDurationMs,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The privacy-preserving SponsorBlock lookup (<c>/api/skipSegments/:sha256Prefix</c>, 4 characters): the
/// service never hears the video id. A 404 means no marks. Answers are cached per video, a failure briefly.
/// </summary>
public sealed class SponsorBlockClient(IHttpClientFactory httpClientFactory, TimeProvider time)
    : ISponsorBlockClient
{
    public const string ClientName = "sponsorblock";
    internal const string Category = "music_offtopic";
    internal static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(6);
    internal static readonly TimeSpan NoneLifetime = TimeSpan.FromHours(1);
    internal static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(2);
    internal const int Capacity = 1000;

    private readonly ConcurrentDictionary<
        string,
        (IReadOnlyList<SponsorBlockSegment> Segments, DateTimeOffset Expires)
    > _cache = new(StringComparer.Ordinal);

    public async Task<YouTubeMusicSection?> ReadMusicSectionAsync(
        string videoId,
        int knownDurationMs,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<SponsorBlockSegment> segments = await SegmentsAsync(
            videoId,
            cancellationToken
        );

        return segments.Count == 0 ? null : YouTubeMusicSection.From(segments, knownDurationMs);
    }

    private async Task<IReadOnlyList<SponsorBlockSegment>> SegmentsAsync(
        string videoId,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = time.GetUtcNow();
        if (
            _cache.TryGetValue(
                videoId,
                out (IReadOnlyList<SponsorBlockSegment> Segments, DateTimeOffset Expires) cached
            )
            && cached.Expires > now
        )
            return cached.Segments;

        (IReadOnlyList<SponsorBlockSegment> segments, TimeSpan lifetime) = await FetchAsync(
            videoId,
            cancellationToken
        );
        if (_cache.Count >= Capacity)
            Evict(now);
        _cache[videoId] = (segments, now + lifetime);

        return segments;
    }

    private async Task<(IReadOnlyList<SponsorBlockSegment> Segments, TimeSpan Lifetime)> FetchAsync(
        string videoId,
        CancellationToken cancellationToken
    )
    {
        string prefix = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(videoId)))[..4]
            .ToLowerInvariant();
        try
        {
            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using HttpResponseMessage response = await client.GetAsync(
                $"https://sponsor.ajay.app/api/skipSegments/{prefix}?category={Category}",
                cancellationToken
            );
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ([], NoneLifetime);
            if (!response.IsSuccessStatusCode)
                return ([], FailureLifetime);

            await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken
            );
            using JsonDocument document = await JsonDocument.ParseAsync(
                body,
                cancellationToken: cancellationToken
            );
            IReadOnlyList<SponsorBlockSegment> segments = Parse(document.RootElement, videoId);

            return (segments, segments.Count == 0 ? NoneLifetime : FoundLifetime);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or OperationCanceledException
                && !cancellationToken.IsCancellationRequested
            )
        {
            return ([], FailureLifetime);
        }
    }

    private static List<SponsorBlockSegment> Parse(JsonElement root, string videoId)
    {
        List<SponsorBlockSegment> found = [];
        if (root.ValueKind != JsonValueKind.Array)
            return found;

        foreach (JsonElement video in root.EnumerateArray())
        {
            if (
                video.ValueKind != JsonValueKind.Object
                || !video.TryGetProperty("videoID", out JsonElement id)
                || id.GetString() != videoId
                || !video.TryGetProperty("segments", out JsonElement segments)
                || segments.ValueKind != JsonValueKind.Array
            )
                continue;

            foreach (JsonElement entry in segments.EnumerateArray())
            {
                if (
                    entry.TryGetProperty("category", out JsonElement category)
                    && category.GetString() != Category
                )
                    continue;
                if (
                    !entry.TryGetProperty("segment", out JsonElement span)
                    || span.ValueKind != JsonValueKind.Array
                    || span.GetArrayLength() != 2
                )
                    continue;

                double duration =
                    entry.TryGetProperty("videoDuration", out JsonElement length)
                    && length.ValueKind == JsonValueKind.Number
                        ? length.GetDouble()
                        : 0;
                found.Add(new(span[0].GetDouble(), span[1].GetDouble(), duration));
            }
        }

        return found;
    }

    private void Evict(DateTimeOffset now)
    {
        foreach (
            KeyValuePair<
                string,
                (IReadOnlyList<SponsorBlockSegment>, DateTimeOffset Expires)
            > entry in _cache
        )
        {
            if (entry.Value.Expires <= now)
                _cache.TryRemove(entry.Key, out _);
        }

        if (_cache.Count >= Capacity)
            _cache.Clear();
    }
}
