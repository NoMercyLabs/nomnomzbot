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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The YouTube Data API key has 10,000 units a day. Once Google says <c>quotaExceeded</c>, chat is told
/// when requests come back, and no further call is spent until the quota resets at midnight Pacific.
/// </summary>
public sealed class YouTubeQuotaExhaustedTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string Link = "https://www.youtube.com/watch?v=" + VideoId;

    private const string QuotaExceededBody = """
        {"error":{"code":403,"message":"The request cannot be completed because you have exceeded your quota.",
          "errors":[{"message":"quota","domain":"youtube.quota","reason":"quotaExceeded"}]}}
        """;

    // 13:00 in Los Angeles (PDT, UTC-7): the quota resets at the next local midnight, 07:00 UTC.
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_used_up_quota_tells_chat_when_requests_resume_and_spends_no_more_calls()
    {
        FakeTimeProvider time = new(Afternoon);
        YouTubeSongRequestIntake intake = new(time: time);
        intake.Http.RespondWhen(_ => true, HttpStatusCode.Forbidden, QuotaExceededBody);

        string first = await intake.SayAsync(Link);
        string second = await intake.SayAsync("never gonna give you up");

        string expected =
            "YouTube's daily request limit is used up; song requests resume after 07:00 UTC.";
        first.Should().Be(expected);
        // A word search runs keyless, so the Data API quota never blocks it; here YouTube answers 403 to
        // everything, so the keyless search is an outage instead.
        second.Should().Be("The music provider is temporarily unavailable.");
        intake.DataApiCalls.Should().Equal("videos");
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Requests_call_youtube_again_once_the_quota_has_reset()
    {
        FakeTimeProvider time = new(Afternoon);
        YouTubeSongRequestIntake intake = new(time: time);
        intake.Http.RespondWhen(_ => true, HttpStatusCode.Forbidden, QuotaExceededBody);
        await intake.SayAsync(Link);

        intake.Http.ClearRoutes();
        intake.RouteVideos(YouTubeSongRequestIntake.Video(VideoId, "Never Gonna Give You Up"));
        time.SetUtcNow(new(2026, 10, 17, 7, 0, 1, TimeSpan.Zero));
        await intake.SayAsync(Link);

        (await intake.QueuedTitlesAsync()).Should().Equal("Never Gonna Give You Up");
    }

    [Fact]
    public async Task A_403_that_is_not_about_quota_stays_an_outage_with_no_cooldown()
    {
        FakeTimeProvider time = new(Afternoon);
        YouTubeSongRequestIntake intake = new(time: time);
        intake.Http.RespondWhen(
            _ => true,
            HttpStatusCode.Forbidden,
            """{"error":{"code":403,"errors":[{"reason":"forbidden"}]}}"""
        );

        string reply = await intake.SayAsync(Link);
        await intake.SayAsync(Link);

        reply.Should().Be("The music provider is temporarily unavailable.");
        intake.DataApiCalls.Should().Equal("videos", "videos");
    }

    [Theory]
    [InlineData("2026-10-16T20:00:00Z", "2026-10-17T07:00:00Z")] // PDT, UTC-7
    [InlineData("2026-12-01T09:00:00Z", "2026-12-02T08:00:00Z")] // PST, UTC-8
    [InlineData("2026-12-01T07:30:00Z", "2026-12-01T08:00:00Z")] // 23:30 the evening before
    public void The_reset_is_the_next_midnight_in_los_angeles(string now, string reset)
    {
        YouTubeQuotaCooldown
            .NextPacificMidnight(DateTimeOffset.Parse(now))
            .Should()
            .Be(DateTimeOffset.Parse(reset));
    }
}
