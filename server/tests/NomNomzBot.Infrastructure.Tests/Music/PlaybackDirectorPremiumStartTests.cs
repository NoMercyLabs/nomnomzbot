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
