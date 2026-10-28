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

using FluentAssertions;
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A widget page that reloads mid-track says so with <see cref="OutputAttached"/>. The director tells the
/// engine that holds the track to bring the track back to the new page; the hub never names an engine.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Playing_OutputAttached_TellsTheHoldingEngineToReattachAndKeepsPlaying(
        bool pageIsPlaying
    )
    {
        Rig rig = new();
        PlaybackItem item = await rig.StartPlaying(EngineKind.YouTube);

        await rig.Director.HandleAsync(new OutputAttached(pageIsPlaying));

        rig.YouTube.Calls.Last().Should().Be(new EngineCall("Reattach", pageIsPlaying.ToString()));
        rig.Spotify.Calls.Should().BeEmpty("only the engine that holds the track is told");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, item));
        PlaybackTransitionedEventLast(rig).Should().Be(("Playing", "Playing", "output_reattached"));
    }

    [Fact]
    public async Task PausedByOwner_OutputAttached_TellsTheHoldingEngineToReattachAndStaysPaused()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.YouTube);
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new OutputAttached(false));

        rig.YouTube.Calls.Last().Should().Be(new EngineCall("Reattach", "False"));
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.PausedByOwner);
    }
}
