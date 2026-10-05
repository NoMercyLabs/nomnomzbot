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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the 10 minute cap (legacy parity): a track longer than ten minutes is refused with
/// <c>TRACK_TOO_LONG</c> in the shared enqueue point, before it reaches the queue or the provider.
/// </summary>
public sealed class MusicServiceTrackLengthTests
{
    private const int TenMinutesMs = 600_000;
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");

    private static (MusicService Sut, RecordingHttpHandler Handler) BuildDefault() =>
        MusicServiceCapacityTests.Build(
            new MusicConfigDto(true, "auto", 50, 50, true, true, "everyone")
        );

    [Fact]
    public async Task A_track_over_ten_minutes_is_refused_before_it_is_queued()
    {
        (MusicService sut, RecordingHttpHandler handler) = BuildDefault();

        Result result = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "long1",
            "viewer1",
            TenMinutesMs + 1
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_TOO_LONG");
        result
            .ErrorData.Should()
            .BeOfType<MusicRequestRefusal>()
            .Which.TrackName.Should()
            .Be("Song long1");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
        MusicServiceCapacityTests
            .QueuePushCount(handler)
            .Should()
            .Be(0, "a refused request must never reach the provider");
    }

    [Fact]
    public async Task A_track_of_exactly_ten_minutes_is_accepted()
    {
        (MusicService sut, RecordingHttpHandler handler) = BuildDefault();

        Result result = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "edge1",
            "viewer1",
            TenMinutesMs
        );

        result.IsSuccess.Should().BeTrue();
        (await sut.GetQueueAsync(ChannelId.ToString()))
            .Queue.Select(q => q.TrackName)
            .Should()
            .Equal("Song edge1");
    }
}
