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
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves operators are never held by a song request limit, the same way they are never held by a
/// command cooldown: the broadcaster and every rung from moderator up skip the length cap, the queue
/// cap and the per-user cap, while a viewer is still refused by each of them.
/// </summary>
public sealed class MusicServiceOperatorBypassTests
{
    private const int OverTenMinutesMs = 600_001;
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");

    private static async Task<Result<MusicTrack>> RequestAsync(
        MusicService sut,
        RecordingHttpHandler handler,
        string trackId,
        string requestedBy,
        PermissionLevel level,
        int durationMs = 200000
    )
    {
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            MusicServiceCapacityTests.SearchJson(trackId, durationMs)
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );
        return await sut.RequestTrackAsync(
            ChannelId.ToString(),
            $"song {trackId}",
            requestedBy,
            level.ToLevelValue()
        );
    }

    [Theory]
    [InlineData(PermissionLevel.Broadcaster)]
    [InlineData(PermissionLevel.Editor)]
    [InlineData(PermissionLevel.LeadModerator)]
    [InlineData(PermissionLevel.Moderator)]
    public async Task An_operators_over_length_request_is_queued_under_their_name(
        PermissionLevel level
    )
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            new MusicConfigDto(true, "auto", 50, 50, true, true, "everyone")
        );

        Result<MusicTrack> result = await RequestAsync(
            sut,
            handler,
            "long1",
            "operator1",
            level,
            OverTenMinutesMs
        );

        result.IsSuccess.Should().BeTrue();
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        queue.Queue.Should().ContainSingle();
        queue.Queue[0].TrackName.Should().Be("Song long1");
        queue.Queue[0].RequestedBy.Should().Be("operator1");
    }

    [Theory]
    [InlineData(PermissionLevel.Everyone)]
    [InlineData(PermissionLevel.Subscriber)]
    [InlineData(PermissionLevel.Vip)]
    public async Task A_non_operators_over_length_request_is_still_refused(PermissionLevel level)
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            new MusicConfigDto(true, "auto", 50, 50, true, true, "everyone")
        );

        Result<MusicTrack> result = await RequestAsync(
            sut,
            handler,
            "long1",
            "viewer1",
            level,
            OverTenMinutesMs
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_TOO_LONG");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_moderator_may_exceed_the_per_user_cap_and_a_viewer_may_not()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            new MusicConfigDto(true, "auto", 50, 1, true, true, "everyone")
        );

        (await RequestAsync(sut, handler, "m1", "mod1", PermissionLevel.Moderator))
            .IsSuccess.Should()
            .BeTrue();
        (await RequestAsync(sut, handler, "m2", "mod1", PermissionLevel.Moderator))
            .IsSuccess.Should()
            .BeTrue();
        (await RequestAsync(sut, handler, "v1", "viewer1", PermissionLevel.Everyone))
            .IsSuccess.Should()
            .BeTrue();
        Result<MusicTrack> viewerSecond = await RequestAsync(
            sut,
            handler,
            "v2",
            "viewer1",
            PermissionLevel.Everyone
        );

        viewerSecond.ErrorCode.Should().Be("PER_USER_LIMIT");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(q => q.TrackName)
            .Should()
            .BeEquivalentTo("Song m1", "Song m2", "Song v1");
    }

    [Fact]
    public async Task The_broadcaster_may_queue_into_a_full_queue_and_a_viewer_may_not()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            new MusicConfigDto(true, "auto", 1, 50, true, true, "everyone")
        );

        (await RequestAsync(sut, handler, "v1", "viewer1", PermissionLevel.Everyone))
            .IsSuccess.Should()
            .BeTrue();
        Result<MusicTrack> viewerBlocked = await RequestAsync(
            sut,
            handler,
            "v2",
            "viewer2",
            PermissionLevel.Everyone
        );
        (await RequestAsync(sut, handler, "b1", "streamer", PermissionLevel.Broadcaster))
            .IsSuccess.Should()
            .BeTrue();

        viewerBlocked.ErrorCode.Should().Be("QUEUE_FULL");
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(q => q.TrackName)
            .Should()
            .Equal("Song v1", "Song b1");
    }
}
