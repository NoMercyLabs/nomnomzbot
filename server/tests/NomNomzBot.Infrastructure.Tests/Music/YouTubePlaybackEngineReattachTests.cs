// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>The YouTube engine puts the current video back on a page that attached or reloaded.</summary>
public sealed class YouTubePlaybackEngineReattachTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReattachOutput_ReplaysTheCurrentVideoWithThePageState(bool pageIsPlaying)
    {
        IYouTubePlayerDispatcher players = Substitute.For<IYouTubePlayerDispatcher>();
        YouTubePlaybackEngine engine = new(
            Broadcaster,
            () => EngineLease.Of(Substitute.For<IMusicProvider>(), players),
            Substitute.For<IYouTubePlayerStateStore>()
        );

        await engine.ReattachOutputAsync(pageIsPlaying, CancellationToken.None);

        await players
            .Received(1)
            .ReplayCurrentAsync(Broadcaster, pageIsPlaying, Arg.Any<CancellationToken>());
    }
}
