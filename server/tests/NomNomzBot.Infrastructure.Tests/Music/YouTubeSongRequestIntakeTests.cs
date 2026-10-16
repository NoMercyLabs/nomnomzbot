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
}
