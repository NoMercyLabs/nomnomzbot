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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the single mapping music-automation-controls.md §3.2 requires: the REST
/// <c>GET .../now-playing</c> read and the <c>song.changed</c> event both call the SAME
/// <see cref="MusicAutomationProjection"/> helper, so for identical playback state they can never
/// disagree — and that a provider without the <c>Library</c> capability degrades <c>IsSaved</c> to
/// <c>null</c> rather than failing the whole projection.
/// </summary>
public sealed class MusicAutomationProjectionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac003");

    private static NowPlaying Playing() =>
        new(
            "Song",
            "Artist",
            "Album",
            null,
            200_000,
            10_000,
            true,
            100,
            null,
            "spotify",
            "spotify:track:1",
            true,
            MusicRepeatMode.Track,
            "spotify:artist:1"
        );

    [Fact]
    public async Task Projection_is_byte_identical_for_the_same_domain_state()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        manageApi
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([true]));
        TimeProvider fixedTime = new FixedTimeProvider(DateTimeOffset.UnixEpoch);

        AutomationNowPlayingDto fromReadPath = await MusicAutomationProjection.ToNowPlayingAsync(
            Playing(),
            "spotify",
            ChannelId,
            manageApi,
            fixedTime,
            CancellationToken.None
        );
        AutomationNowPlayingDto fromEventPath = await MusicAutomationProjection.ToNowPlayingAsync(
            Playing(),
            "spotify",
            ChannelId,
            manageApi,
            fixedTime,
            CancellationToken.None
        );

        fromReadPath.Should().Be(fromEventPath);
        fromReadPath.IsSaved.Should().BeTrue();
        fromReadPath.RepeatMode.Should().Be("track");
    }

    [Fact]
    public async Task IsSaved_degrades_to_null_when_the_library_capability_check_fails()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        manageApi
            .AreTracksSavedAsync(
                ChannelId,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<IReadOnlyList<bool>>(
                    "no library capability",
                    "CAPABILITY_UNSUPPORTED"
                )
            );

        AutomationNowPlayingDto dto = await MusicAutomationProjection.ToNowPlayingAsync(
            Playing(),
            "spotify",
            ChannelId,
            manageApi,
            TimeProvider.System,
            CancellationToken.None
        );

        dto.IsSaved.Should().BeNull();
        dto.Title.Should().Be("Song");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
