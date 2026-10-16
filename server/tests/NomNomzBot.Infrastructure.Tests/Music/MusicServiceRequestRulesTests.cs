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
/// Proves the three channel request dials in the shared enqueue point: the longest song
/// (<c>MaxSongSeconds</c>), the explicit block (<c>AllowExplicit</c>) and the per-stream cap
/// (<c>PerStreamLimit</c>). Each rule refuses with its own code and the configured limit, and accepts
/// at the exact edge.
/// </summary>
public sealed class MusicServiceRequestRulesTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ad001");

    private static (
        MusicService Sut,
        RecordingHttpHandler Handler,
        ISongRequestQueueStore Store
    ) Build(int maxSongSeconds = 360, bool allowExplicit = true, int? perStreamLimit = null)
    {
        SongRequestQueueStore store = new();
        (MusicService sut, RecordingHttpHandler handler) = MusicServiceCapacityTests.Build(
            store: store,
            config: new MusicConfigDto(
                true,
                "auto",
                50,
                50,
                true,
                true,
                "everyone",
                MaxSongSeconds: maxSongSeconds,
                AllowExplicit: allowExplicit,
                PerStreamLimit: perStreamLimit
            )
        );
        return (sut, handler, store);
    }

    [Fact]
    public async Task A_song_one_second_over_the_configured_length_is_refused_with_the_limit()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build(maxSongSeconds: 120);

        Result result = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "len1",
            "viewer1",
            durationMs: 121_000
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_TOO_LONG");
        result.ErrorData.Should().BeOfType<MusicRequestRefusal>().Which.Limit.Should().Be(120);
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_song_exactly_as_long_as_the_configured_length_is_accepted()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build(maxSongSeconds: 120);

        Result result = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "len2",
            "viewer1",
            durationMs: 120_000
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_explicit_song_is_refused_when_explicit_is_not_allowed()
    {
        (MusicService sut, RecordingHttpHandler handler, _) = Build(allowExplicit: false);

        Result result = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "exp1",
            "viewer1",
            isExplicit: true
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TRACK_EXPLICIT");
        result
            .ErrorData.Should()
            .BeOfType<MusicRequestRefusal>()
            .Which.TrackName.Should()
            .Be("Song exp1");
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_clean_song_is_accepted_when_explicit_is_not_allowed_and_an_explicit_one_when_it_is()
    {
        (MusicService blocking, RecordingHttpHandler blockingHandler, _) = Build(
            allowExplicit: false
        );
        (MusicService open, RecordingHttpHandler openHandler, _) = Build(allowExplicit: true);

        (
            await MusicServiceCapacityTests.RequestAsync(
                blocking,
                blockingHandler,
                "exp2",
                "viewer1",
                isExplicit: false
            )
        )
            .IsSuccess.Should()
            .BeTrue();
        (
            await MusicServiceCapacityTests.RequestAsync(
                open,
                openHandler,
                "exp3",
                "viewer1",
                isExplicit: true
            )
        )
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public async Task A_requester_over_the_per_stream_limit_is_refused_even_after_their_songs_played()
    {
        (MusicService sut, RecordingHttpHandler handler, ISongRequestQueueStore store) = Build(
            perStreamLimit: 2
        );

        (await MusicServiceCapacityTests.RequestAsync(sut, handler, "ps1", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await MusicServiceCapacityTests.RequestAsync(sut, handler, "ps2", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();

        // Both songs played and left the queue: the stream tally still holds them.
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Clear();

        Result third = await MusicServiceCapacityTests.RequestAsync(sut, handler, "ps3", "viewer1");

        third.IsFailure.Should().BeTrue();
        third.ErrorCode.Should().Be("PER_STREAM_LIMIT");
        third.ErrorData.Should().BeOfType<MusicRequestRefusal>().Which.Limit.Should().Be(2);

        Result otherViewer = await MusicServiceCapacityTests.RequestAsync(
            sut,
            handler,
            "ps4",
            "viewer2"
        );
        otherViewer.IsSuccess.Should().BeTrue("the cap is per requester");
    }

    [Fact]
    public async Task A_new_stream_resets_the_per_stream_tally()
    {
        (MusicService sut, RecordingHttpHandler handler, ISongRequestQueueStore store) = Build(
            perStreamLimit: 1
        );
        (await MusicServiceCapacityTests.RequestAsync(sut, handler, "rs1", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelId.ToString());
        queue.Clear();

        store.ResetStreamRequests(ChannelId.ToString());

        (await MusicServiceCapacityTests.RequestAsync(sut, handler, "rs2", "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public async Task With_no_per_stream_limit_a_requester_is_not_counted_against_one()
    {
        (MusicService sut, RecordingHttpHandler handler, ISongRequestQueueStore store) = Build();
        for (int i = 1; i <= 3; i++)
        {
            (await MusicServiceCapacityTests.RequestAsync(sut, handler, $"no{i}", "viewer1"))
                .IsSuccess.Should()
                .BeTrue();
        }

        store.StreamRequestCount(ChannelId.ToString(), "viewer1").Should().Be(3);
    }
}
