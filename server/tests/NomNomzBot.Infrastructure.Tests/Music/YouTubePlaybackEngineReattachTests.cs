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
