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
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Services.Analytics;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Stoney, 2026-11-10: "we need analytics and all those features we have from twitch to match". The Analytics
/// page reads the channel's own YouTube numbers per day from the YouTube Analytics API (reports.query).
/// </summary>
public sealed class YouTubeAnalyticsServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000a7a17");
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 2);

    // The shape reports.query answers with: the column headers name each value of a row, in order.
    private const string TableWithRevenue = """
        {
          "kind": "youtubeAnalytics#resultTable",
          "columnHeaders": [
            { "name": "day", "columnType": "DIMENSION", "dataType": "STRING" },
            { "name": "views", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "estimatedMinutesWatched", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "subscribersGained", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "subscribersLost", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "estimatedRevenue", "columnType": "METRIC", "dataType": "FLOAT" }
          ],
          "rows": [
            ["2026-11-01", 120, 340, 5, 1, 2.75],
            ["2026-11-02", 80, 150, 2, 0, 1.1]
          ]
        }
        """;

    private const string TableWithoutRevenue = """
        {
          "kind": "youtubeAnalytics#resultTable",
          "columnHeaders": [
            { "name": "day", "columnType": "DIMENSION", "dataType": "STRING" },
            { "name": "subscribersLost", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "views", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "estimatedMinutesWatched", "columnType": "METRIC", "dataType": "INTEGER" },
            { "name": "subscribersGained", "columnType": "METRIC", "dataType": "INTEGER" }
          ],
          "rows": [["2026-11-01", 1, 120, 340, 5]]
        }
        """;

    private static (YouTubeAnalyticsService Sut, RecordingHttpHandler Http) Build(
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
                NullLogger<YouTubeAnalyticsService>.Instance
            ),
            http
        );
    }

    private static bool AsksForRevenue(HttpRequestMessage request) =>
        request.RequestUri!.Query.Contains("estimatedRevenue", StringComparison.Ordinal);

    [Fact]
    public async Task A_monetised_channel_gets_each_day_with_views_watch_time_subscribers_and_revenue()
    {
        (YouTubeAnalyticsService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(AsksForRevenue, HttpStatusCode.OK, TableWithRevenue);

        Result<YouTubeAnalyticsDto> result = await sut.GetDailyAsync(Channel, From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeAnalyticsAccess.Allowed);
        result.Value.RevenueAvailable.Should().BeTrue();
        result
            .Value.Days.Should()
            .Equal(
                new YouTubeAnalyticsDayDto(new(2026, 11, 1), 120, 340, 5, 1, 2.75m),
                new YouTubeAnalyticsDayDto(new(2026, 11, 2), 80, 150, 2, 0, 1.1m)
            );
        http.RequestUrls.Should().ContainSingle();
        string url = http.RequestUrls[0];
        url.Should().StartWith("GET https://youtubeanalytics.googleapis.com/v2/reports?");
        url.Should().Contain("ids=channel%3D%3DMINE");
        url.Should().Contain("startDate=2026-11-01").And.Contain("endDate=2026-11-02");
        url.Should().Contain("dimensions=day").And.Contain("sort=day");
    }

    [Fact]
    public async Task A_channel_that_is_not_monetised_gets_its_other_numbers_and_no_revenue()
    {
        (YouTubeAnalyticsService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(AsksForRevenue, HttpStatusCode.Forbidden, """{"error":{"code":403}}""");
        http.RespondWhen(_ => true, HttpStatusCode.OK, TableWithoutRevenue);

        Result<YouTubeAnalyticsDto> result = await sut.GetDailyAsync(Channel, From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.RevenueAvailable.Should().BeFalse();
        result
            .Value.Days.Should()
            .Equal(new YouTubeAnalyticsDayDto(new(2026, 11, 1), 120, 340, 5, 1, null));
        http.RequestUrls.Should().HaveCount(2, "the second read leaves the revenue out");
    }

    [Fact]
    public async Task A_channel_that_did_not_allow_analytics_gets_a_reason_it_can_act_on()
    {
        (YouTubeAnalyticsService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(_ => true, HttpStatusCode.Forbidden, """{"error":{"code":403}}""");

        Result<YouTubeAnalyticsDto> result = await sut.GetDailyAsync(Channel, From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeAnalyticsAccess.NotAllowed);
        result.Value.Days.Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_without_a_YouTube_connection_asks_Google_nothing()
    {
        (YouTubeAnalyticsService sut, RecordingHttpHandler http) = Build(accessToken: null);

        Result<YouTubeAnalyticsDto> result = await sut.GetDailyAsync(Channel, From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Access.Should().Be(YouTubeAnalyticsAccess.NotConnected);
        http.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_range_with_no_activity_is_an_empty_list_not_an_error()
    {
        (YouTubeAnalyticsService sut, RecordingHttpHandler http) = Build();
        http.RespondWhen(
            _ => true,
            HttpStatusCode.OK,
            """{"kind":"youtubeAnalytics#resultTable","columnHeaders":[]}"""
        );

        Result<YouTubeAnalyticsDto> result = await sut.GetDailyAsync(Channel, From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().BeEmpty();
    }
}
