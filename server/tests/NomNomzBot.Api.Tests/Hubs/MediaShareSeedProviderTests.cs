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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A reloaded media_share page picks the clip back up: the playing item is loaded again, moved to where it was,
/// at its volume, and paused when it was paused. With nothing playing the page gets no frames and stays idle.
/// </summary>
public sealed class MediaShareSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid RequestId = Guid.CreateVersion7();

    private static readonly MediaShareRequestDto Playing = new(
        RequestId,
        Guid.CreateVersion7(),
        "Viewer",
        null,
        "youtube",
        "https://youtu.be/abc",
        "abc",
        "A clip",
        90,
        null,
        "playing",
        null,
        DateTime.UtcNow
    );

    private static MediaShareSeedProvider Build(
        MediaShareRequestDto? playing,
        MediaPlayerStateDto state
    )
    {
        IMediaShareService queue = Substitute.For<IMediaShareService>();
        queue
            .GetQueueAsync(
                Broadcaster,
                Arg.Any<MediaShareFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new PagedList<MediaShareRequestDto>(
                        playing is null ? [] : [playing],
                        1,
                        1,
                        playing is null ? 0 : 1
                    )
                )
            );
        IMediaSharePlaybackService playback = Substitute.For<IMediaSharePlaybackService>();
        playback.GetState(Broadcaster).Returns(state);
        return new(queue, playback);
    }

    [Fact]
    public async Task A_paused_clip_comes_back_loaded_at_its_position_volume_and_paused()
    {
        MediaShareSeedProvider sut = Build(
            Playing,
            new("paused", RequestId, "A clip", "Viewer", null, 42, 90, 60)
        );

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            new Widget { BroadcasterId = Broadcaster },
            CancellationToken.None
        );

        List<MediaShareCommandWidgetPayload> commands =
        [
            .. frames.Select(f => (MediaShareCommandWidgetPayload)f.Data!),
        ];
        frames.Should().OnlyContain(f => f.EventType == "media_share_command");
        commands.Select(c => c.Command).Should().Equal("load", "seek", "volume", "pause");
        commands[0].RequestId.Should().Be(RequestId);
        commands[0].MediaRef.Should().Be("abc");
        commands[0].SourceType.Should().Be("youtube");
        commands[1].PositionSeconds.Should().Be(42);
        commands[2].Volume.Should().Be(60);
    }

    [Fact]
    public async Task Nothing_playing_gives_no_frames()
    {
        MediaShareSeedProvider sut = Build(null, MediaPlayerStateDto.Idle);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            new Widget { BroadcasterId = Broadcaster },
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }
}
