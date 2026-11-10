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
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Community;

/// <summary>
/// Reads the channel's members from <c>members.list</c> and its levels from <c>membershipsLevels.list</c>
/// (YouTube Data API v3, read against the live docs 2026-11-10). Both accept only the
/// <c>youtube.channel-memberships.creator</c> scope. A channel without memberships answers 400
/// <c>channelMembershipsNotEnabled</c>. Google also gives <c>members.list</c> only to apps it approved, so a
/// 403 means "not allowed" whichever of the two it is.
/// </summary>
public sealed class YouTubeMembersService(
    IHttpClientFactory httpClientFactory,
    IYouTubeAccessTokenProvider tokens,
    ICacheService cache,
    ILogger<YouTubeMembersService> logger
) : IYouTubeMembersService
{
    private const string ApiBase = "https://www.googleapis.com/youtube/v3";
    private const int MaxPageSize = 1000;

    // Every read costs Google quota, and a count is asked on every overlay load and stats refresh.
    private static readonly TimeSpan CountTtl = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http = httpClientFactory.CreateClient("youtube");

    public Task<Result<YouTubeMembersDto>> GetMembersAsync(
        Guid broadcasterId,
        string? pageToken,
        int take,
        CancellationToken ct = default
    ) => PageAsync(broadcasterId, pageToken, take, withLevels: string.IsNullOrEmpty(pageToken), ct);

    public async Task<Result<int>> GetMemberCountAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        string key = $"youtube:members:count:{broadcasterId}";
        if (await cache.GetAsync<MemberCount>(key, ct) is { } kept)
            return Result.Success(kept.Value);

        Result<YouTubeMembersDto> page = await PageAsync(
            broadcasterId,
            null,
            MaxPageSize,
            withLevels: false,
            ct
        );
        if (page.IsFailure || page.Value.Access != YouTubeMembersAccess.Allowed)
            return Result.Failure<int>(
                page.ErrorMessage ?? "The YouTube members cannot be read.",
                page.ErrorCode ?? "NOT_AVAILABLE"
            );

        // A list that fits one page is counted here; a longer one takes the total YouTube reports.
        int count = page.Value.NextPageToken is null ? page.Value.Members.Count : page.Value.Total;
        await cache.SetAsync(key, new MemberCount(count), CountTtl, ct);
        return Result.Success(count);
    }

    private async Task<Result<YouTubeMembersDto>> PageAsync(
        Guid broadcasterId,
        string? pageToken,
        int take,
        bool withLevels,
        CancellationToken ct
    )
    {
        string? accessToken = await tokens.GetAccessTokenAsync(broadcasterId, ct);
        if (accessToken is null)
            return Refused(YouTubeMembersAccess.NotConnected);

        string url =
            $"{ApiBase}/members?part=snippet&mode=all_current"
            + $"&maxResults={Math.Clamp(take, 1, MaxPageSize)}";
        if (!string.IsNullOrEmpty(pageToken))
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

        (HttpStatusCode? status, MemberListResponse? body) = await GetAsync<MemberListResponse>(
            url,
            accessToken,
            ct
        );
        switch (status)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.BadRequest:
                return Refused(YouTubeMembersAccess.NotEnabled);
            case HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized:
                return Refused(YouTubeMembersAccess.NotAllowed);
            default:
                return Result.Failure<YouTubeMembersDto>(
                    "YouTube is temporarily unavailable.",
                    "SERVICE_UNAVAILABLE"
                );
        }

        IReadOnlyList<YouTubeMembershipLevelDto> levels = withLevels
            ? await LevelsAsync(accessToken, ct)
            : [];
        List<YouTubeMemberDto> members =
        [
            .. (body?.Items ?? []).Where(item => item.Snippet is not null).Select(Member),
        ];

        return Result.Success(
            new YouTubeMembersDto(
                YouTubeMembersAccess.Allowed,
                members,
                levels,
                body?.PageInfo?.TotalResults ?? members.Count,
                body?.NextPageToken
            )
        );
    }

    private static Result<YouTubeMembersDto> Refused(string access) =>
        Result.Success(new YouTubeMembersDto(access, [], [], 0, null));

    /// <summary>The level names are an extra: a failed read leaves them out and keeps the members.</summary>
    private async Task<IReadOnlyList<YouTubeMembershipLevelDto>> LevelsAsync(
        string accessToken,
        CancellationToken ct
    )
    {
        (HttpStatusCode? _, LevelListResponse? body) = await GetAsync<LevelListResponse>(
            $"{ApiBase}/membershipsLevels?part=id,snippet",
            accessToken,
            ct
        );

        return
        [
            .. (body?.Items ?? [])
                .Where(level => !string.IsNullOrEmpty(level.Id))
                .Select(level => new YouTubeMembershipLevelDto(
                    level.Id!,
                    level.Snippet?.LevelDetails?.DisplayName ?? level.Id!
                )),
        ];
    }

    private static YouTubeMemberDto Member(MemberItem item)
    {
        MemberDetails? who = item.Snippet!.MemberDetails;
        MembershipsDetails? membership = item.Snippet.MembershipsDetails;

        return new(
            who?.ChannelId,
            who?.DisplayName ?? string.Empty,
            who?.ProfileImageUrl,
            membership?.HighestAccessibleLevel,
            membership?.HighestAccessibleLevelDisplayName,
            membership?.MembershipsDuration?.MemberSince,
            membership?.MembershipsDuration?.MemberTotalDurationMonths ?? 0
        );
    }

    private async Task<(HttpStatusCode? Status, T? Body)> GetAsync<T>(
        string url,
        string accessToken,
        CancellationToken ct
    )
        where T : class
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "YouTube members read failed: {Status} for {Path}",
                    response.StatusCode,
                    new Uri(url).AbsolutePath
                );
                return (response.StatusCode, null);
            }

            return (
                response.StatusCode,
                await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "YouTube members read threw for {Path}", new Uri(url).AbsolutePath);
            return (null, null);
        }
    }

    /// <summary>The kept count, as a type a cache can hold and tell from "nothing kept".</summary>
    private sealed record MemberCount(int Value);

    private sealed class MemberListResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("pageInfo")]
        public PageInfo? PageInfo { get; set; }

        [JsonPropertyName("items")]
        public List<MemberItem>? Items { get; set; }
    }

    private sealed class PageInfo
    {
        [JsonPropertyName("totalResults")]
        public int? TotalResults { get; set; }
    }

    private sealed class MemberItem
    {
        [JsonPropertyName("snippet")]
        public MemberSnippet? Snippet { get; set; }
    }

    private sealed class MemberSnippet
    {
        [JsonPropertyName("memberDetails")]
        public MemberDetails? MemberDetails { get; set; }

        [JsonPropertyName("membershipsDetails")]
        public MembershipsDetails? MembershipsDetails { get; set; }
    }

    private sealed class MemberDetails
    {
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("profileImageUrl")]
        public string? ProfileImageUrl { get; set; }
    }

    private sealed class MembershipsDetails
    {
        [JsonPropertyName("highestAccessibleLevel")]
        public string? HighestAccessibleLevel { get; set; }

        [JsonPropertyName("highestAccessibleLevelDisplayName")]
        public string? HighestAccessibleLevelDisplayName { get; set; }

        [JsonPropertyName("membershipsDuration")]
        public MembershipsDuration? MembershipsDuration { get; set; }
    }

    private sealed class MembershipsDuration
    {
        [JsonPropertyName("memberSince")]
        public DateTimeOffset? MemberSince { get; set; }

        [JsonPropertyName("memberTotalDurationMonths")]
        public int? MemberTotalDurationMonths { get; set; }
    }

    private sealed class LevelListResponse
    {
        [JsonPropertyName("items")]
        public List<LevelItem>? Items { get; set; }
    }

    private sealed class LevelItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public LevelSnippet? Snippet { get; set; }
    }

    private sealed class LevelSnippet
    {
        [JsonPropertyName("levelDetails")]
        public LevelDetails? LevelDetails { get; set; }
    }

    private sealed class LevelDetails
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}
