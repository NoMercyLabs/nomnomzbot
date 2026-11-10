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
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community;

/// <summary>
/// Stoney, 2026-11-10: "we need analytics and all those features we have from twitch to match". The YouTube
/// channel members are what Twitch subscribers are: the bot reads who they are, their level and since when
/// (members.list and membershipsLevels.list, read against the live docs 2026-11-10).
/// </summary>
public sealed class YouTubeMembersServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000e3be5");

    private const string MembersPage = """
        {
          "kind": "youtube#memberListResponse",
          "nextPageToken": "page-2",
          "pageInfo": { "totalResults": 42, "resultsPerPage": 2 },
          "items": [
            {
              "kind": "youtube#member",
              "snippet": {
                "creatorChannelId": "UCcreator",
                "memberDetails": {
                  "channelId": "UCmember1",
                  "channelUrl": "https://www.youtube.com/channel/UCmember1",
                  "displayName": "Marci",
                  "profileImageUrl": "https://yt3.example/marci.jpg"
                },
                "membershipsDetails": {
                  "highestAccessibleLevel": "level-gold",
                  "highestAccessibleLevelDisplayName": "Gold",
                  "accessibleLevels": ["level-gold", "level-fan"],
                  "membershipsDuration": {
                    "memberSince": "2026-03-01T10:00:00Z",
                    "memberTotalDurationMonths": 8
                  }
                }
              }
            },
            {
              "kind": "youtube#member",
              "snippet": {
                "creatorChannelId": "UCcreator",
                "memberDetails": { "displayName": "A deleted channel" },
                "membershipsDetails": {
                  "highestAccessibleLevel": "level-fan",
                  "highestAccessibleLevelDisplayName": "Fan",
                  "membershipsDuration": {
                    "memberSince": "2026-10-20T08:30:00Z",
                    "memberTotalDurationMonths": 0
                  }
                }
              }
            }
          ]
        }
        """;

    private const string Levels = """
        {
          "kind": "youtube#membershipsLevelListResponse",
          "items": [
            { "id": "level-fan", "snippet": { "levelDetails": { "displayName": "Fan" } } },
            { "id": "level-gold", "snippet": { "levelDetails": { "displayName": "Gold" } } }
          ]
        }
        """;

    private static (YouTubeMembersService Sut, RecordingHttpHandler Http) Build(
        string? accessToken = "token-1"
    )
    {
        RecordingHttpHandler http = new();
        IYouTubeAccessTokenProvider tokens = Substitute.For<IYouTubeAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Channel, Arg.Any<CancellationToken>()).Returns(accessToken);

        return (
            new(
                new SingleHandlerClientFactory(http),
                tokens,
                NullLogger<YouTubeMembersService>.Instance
            ),
            http
        );
    }

    private static bool IsMembers(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/youtube/v3/members", StringComparison.Ordinal);

    private static bool IsLevels(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith(
            "/youtube/v3/membershipsLevels",
            StringComparison.Ordinal
        );

    [Fact]
    public async Task A_page_of_members_names_each_member_with_level_and_member_since_and_the_levels()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(IsMembers, HttpStatusCode.OK, MembersPage);
        http.RespondWhen(IsLevels, HttpStatusCode.OK, Levels);

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, null, 2);

        result.IsSuccess.Should().BeTrue();
        YouTubeMembersDto page = result.Value;
        page.Access.Should().Be(YouTubeMembersAccess.Allowed);
        page.Total.Should().Be(42);
        page.NextPageToken.Should().Be("page-2");
        page.Members.Should()
            .Equal(
                new YouTubeMemberDto(
                    "UCmember1",
                    "Marci",
                    "https://yt3.example/marci.jpg",
                    "level-gold",
                    "Gold",
                    new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
                    8
                ),
                new YouTubeMemberDto(
                    null,
                    "A deleted channel",
                    null,
                    "level-fan",
                    "Fan",
                    new(2026, 10, 20, 8, 30, 0, TimeSpan.Zero),
                    0
                )
            );
        page.Levels.Should()
            .Equal(
                new YouTubeMembershipLevelDto("level-fan", "Fan"),
                new YouTubeMembershipLevelDto("level-gold", "Gold")
            );
        http.RequestUrls.Should()
            .Contain(url =>
                url.StartsWith(
                    "GET https://www.googleapis.com/youtube/v3/members?",
                    StringComparison.Ordinal
                )
                && url.Contains("part=snippet", StringComparison.Ordinal)
                && url.Contains("mode=all_current", StringComparison.Ordinal)
                && url.Contains("maxResults=2", StringComparison.Ordinal)
                && !url.Contains("pageToken", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task The_next_page_is_asked_with_its_token_and_does_not_read_the_levels_again()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(IsMembers, HttpStatusCode.OK, MembersPage);

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, "page-2", 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Levels.Should().BeEmpty();
        http.RequestUrls.Should().ContainSingle();
        http.RequestUrls[0].Should().Contain("pageToken=page-2").And.Contain("maxResults=50");
    }

    [Fact]
    public async Task A_channel_without_memberships_is_told_so_and_is_not_an_error()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(
            IsMembers,
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"errors":[{"reason":"channelMembershipsNotEnabled"}]}}"""
        );

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, null, 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeMembersAccess.NotEnabled);
        result.Value.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_that_did_not_allow_the_member_list_or_that_Google_gave_no_access_is_told_so()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(IsMembers, HttpStatusCode.Forbidden, """{"error":{"code":403}}""");

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, null, 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeMembersAccess.NotAllowed);
    }

    [Fact]
    public async Task A_channel_without_a_YouTube_connection_asks_Google_nothing()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build(accessToken: null);

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, null, 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeMembersAccess.NotConnected);
        http.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Google_outage_is_a_failure_the_page_can_retry()
    {
        (YouTubeMembersService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(IsMembers, HttpStatusCode.ServiceUnavailable);

        Result<YouTubeMembersDto> result = await sut.GetMembersAsync(Channel, null, 50);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }
}
