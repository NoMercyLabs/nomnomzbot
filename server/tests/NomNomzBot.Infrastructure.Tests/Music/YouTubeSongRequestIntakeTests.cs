// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// What a viewer in a YouTube-only channel sees in chat after <c>!sr</c>, and what lands in the queue:
/// a video the overlay player can never play is refused with its own reason, never "Added".
/// </summary>
public sealed class YouTubeSongRequestIntakeTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string Link = "https://www.youtube.com/watch?v=" + VideoId;

    [Fact]
    public async Task A_video_that_cannot_be_embedded_is_refused_and_not_queued()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video(VideoId, "Locked Song", embeddable: false)
        );

        string reply = await intake.SayAsync(Link);

        reply
            .Should()
            .Be(
                "\"Locked Song\" can't be played here — its uploader doesn't allow it on other sites. Try another upload of the song."
            );
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_age_restricted_video_is_refused_and_not_queued_even_for_a_moderator()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video(
                VideoId,
                "Mature Song",
                extraContentDetails: "\"contentRating\":{\"ytRating\":\"ytAgeRestricted\"}"
            )
        );

        string reply = await intake.SayAsync(Link, roleLevel: 10);

        reply
            .Should()
            .Be(
                "\"Mature Song\" is age-restricted on YouTube, so it can't play on stream. Try another upload of the song."
            );
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_playable_video_is_queued()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(YouTubeSongRequestIntake.Video(VideoId, "Never Gonna Give You Up"));

        await intake.SayAsync(Link);

        (await intake.QueuedTitlesAsync()).Should().Equal("Never Gonna Give You Up");
    }

    [Theory]
    [InlineData("live")]
    [InlineData("upcoming")]
    public async Task A_live_stream_or_premiere_link_gets_its_own_reply_and_nothing_is_searched(
        string broadcast
    )
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video(
                VideoId,
                "24/7 Lofi Radio",
                duration: "P0D",
                live: broadcast
            )
        );
        intake.RouteSearch("aaaaaaaaaaa");

        string reply = await intake.SayAsync(Link);

        reply
            .Should()
            .Be("Live streams and premieres can't be requested. Send a link to a regular video.");
        intake.DataApiCalls.Should().Equal("videos");
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_to_a_private_or_deleted_video_gets_its_own_reply_and_nothing_is_searched()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(); // videos.list answers an empty item set for private and deleted videos alike.
        intake.RouteSearch("aaaaaaaaaaa");

        string reply = await intake.SayAsync(Link);

        reply.Should().Be("That video doesn't exist or is private.");
        intake.DataApiCalls.Should().Equal("videos");
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_dashboard_add_refuses_a_live_stream_instead_of_queuing_a_placeholder()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(YouTubeSongRequestIntake.Video(VideoId, "LIVE", live: "live"));

        NomNomzBot.Application.Common.Models.Result result = await intake.Music.AddToQueueAsync(
            YouTubeSongRequestIntake.ChannelId.ToString(),
            Link,
            "viewer1"
        );

        result.ErrorCode.Should().Be("TRACK_IS_LIVE");
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_video_blocked_in_every_country_is_refused_and_not_queued()
    {
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video(
                VideoId,
                "Withdrawn Song",
                extraContentDetails: "\"regionRestriction\":{\"allowed\":[]}"
            )
        );

        string reply = await intake.SayAsync(Link);

        reply
            .Should()
            .Be(
                "That track can't play in the streamer's country. Try a different version of the song."
            );
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_video_blocked_only_in_some_countries_is_still_queued()
    {
        // No channel country setting exists yet, so a partial block cannot be judged; only a video that
        // plays nowhere is refused.
        YouTubeSongRequestIntake intake = new();
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video(
                VideoId,
                "Mostly Fine",
                extraContentDetails: "\"regionRestriction\":{\"blocked\":[\"DE\"]}"
            )
        );

        await intake.SayAsync(Link);

        (await intake.QueuedTitlesAsync()).Should().Equal("Mostly Fine");
    }
}
