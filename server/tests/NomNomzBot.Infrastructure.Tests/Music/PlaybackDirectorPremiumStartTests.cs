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

/// <summary>A start the Spotify account cannot do (no Premium) ends Blocked with the premium reason and keeps the head.</summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task Starting_WhenTheEngineAnswersPremiumRequired_BlocksWithThePremiumReasonAndKeepsTheHead()
    {
        Rig rig = new();
        rig.Spotify.StartResult = EngineStartResult.BlockedBy(EngineBlockReason.PremiumRequired);

        await rig.Request(Item("a"));

        rig.Director.State.Should().Be(PlaybackState.Blocked("PremiumRequired"));
        rig.QueueIds.Should().Equal("a");
        rig.Journal.Last().Reason.Should().Be("start_refused");
    }
}
