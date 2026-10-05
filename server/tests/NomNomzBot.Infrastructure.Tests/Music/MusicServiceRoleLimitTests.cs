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
/// Proves the per-role song request cap: a requester gets the highest cap among the rungs their level
/// reaches, a rung with no value falls back to <c>MaxRequestsPerUser</c>, a request with no level (the
/// dashboard, the public page) keeps the channel cap, and a refusal reports the cap that applied.
/// </summary>
public sealed class MusicServiceRoleLimitTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");

    private static MusicConfigDto Config(Dictionary<string, int>? perRole) =>
        new(true, "auto", 50, 1, true, true, "everyone", MaxRequestsPerRole: perRole);

    private static int Level(PermissionLevel level) => level.ToLevelValue();

    private static async Task<Result<MusicTrack>> RequestAsync(
        MusicService sut,
        RecordingHttpHandler handler,
        string trackId,
        string requestedBy,
        int? roleLevel
    )
    {
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            MusicServiceCapacityTests.SearchJson(trackId)
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
            roleLevel
        );
    }

    [Fact]
    public async Task A_moderator_gets_the_moderator_cap_while_a_viewer_keeps_the_channel_cap()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            Config(new() { ["moderator"] = 3 })
        );
        int viewer = Level(PermissionLevel.Everyone);
        int moderator = Level(PermissionLevel.Moderator);

        (await RequestAsync(sut, handler, "v1", "viewer1", viewer)).IsSuccess.Should().BeTrue();
        Result<MusicTrack> viewerSecond = await RequestAsync(sut, handler, "v2", "viewer1", viewer);
        viewerSecond.IsFailure.Should().BeTrue();
        viewerSecond.ErrorCode.Should().Be("PER_USER_LIMIT");
        ((MusicRequestRefusal)viewerSecond.ErrorData!).Limit.Should().Be(1);

        (await RequestAsync(sut, handler, "m1", "mod1", moderator)).IsSuccess.Should().BeTrue();
        (await RequestAsync(sut, handler, "m2", "mod1", moderator)).IsSuccess.Should().BeTrue();
        (await RequestAsync(sut, handler, "m3", "mod1", moderator)).IsSuccess.Should().BeTrue();
        Result<MusicTrack> modFourth = await RequestAsync(sut, handler, "m4", "mod1", moderator);
        modFourth.IsFailure.Should().BeTrue();
        modFourth.ErrorCode.Should().Be("PER_USER_LIMIT");
        ((MusicRequestRefusal)modFourth.ErrorData!).Limit.Should().Be(3);

        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(q => q.TrackName)
            .Should()
            .BeEquivalentTo("Song v1", "Song m1", "Song m2", "Song m3");
    }

    [Fact]
    public async Task A_role_with_no_value_falls_back_to_the_channel_cap()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            Config(new() { ["moderator"] = 3 })
        );
        int vip = Level(PermissionLevel.Vip);

        (await RequestAsync(sut, handler, "p1", "vip1", vip)).IsSuccess.Should().BeTrue();
        Result<MusicTrack> second = await RequestAsync(sut, handler, "p2", "vip1", vip);

        second.ErrorCode.Should().Be("PER_USER_LIMIT");
        ((MusicRequestRefusal)second.ErrorData!).Limit.Should().Be(1);
    }

    [Fact]
    public async Task A_moderator_gets_the_highest_cap_of_the_rungs_their_level_reaches()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            Config(new() { ["subscriber"] = 4, ["moderator"] = 2 })
        );
        int moderator = Level(PermissionLevel.Moderator);

        for (int i = 1; i <= 4; i++)
            (await RequestAsync(sut, handler, $"h{i}", "mod1", moderator))
                .IsSuccess.Should()
                .BeTrue();
        Result<MusicTrack> fifth = await RequestAsync(sut, handler, "h5", "mod1", moderator);

        ((MusicRequestRefusal)fifth.ErrorData!).Limit.Should().Be(4);
    }

    [Fact]
    public async Task A_request_with_no_role_level_keeps_the_channel_cap()
    {
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            Config(new() { ["broadcaster"] = 9, ["viewer"] = 9 })
        );

        (await RequestAsync(sut, handler, "n1", "dash1", null)).IsSuccess.Should().BeTrue();
        Result<MusicTrack> second = await RequestAsync(sut, handler, "n2", "dash1", null);

        second.ErrorCode.Should().Be("PER_USER_LIMIT");
        ((MusicRequestRefusal)second.ErrorData!).Limit.Should().Be(1);
    }
}
