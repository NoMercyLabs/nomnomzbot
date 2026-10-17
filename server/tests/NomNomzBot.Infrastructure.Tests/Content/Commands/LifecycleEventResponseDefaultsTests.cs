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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Content.Commands;

namespace NomNomzBot.Infrastructure.Tests.Content.Commands;

/// <summary>A fresh seed writes the poll start, prediction and hype train rows as platform defaults that ship OFF.</summary>
public sealed class LifecycleEventResponseDefaultsTests
{
    [Theory]
    [InlineData("channel.poll.begin")]
    [InlineData("channel.prediction.begin")]
    [InlineData("channel.prediction.lock")]
    [InlineData("channel.prediction.end")]
    [InlineData("channel.hype_train.begin")]
    [InlineData("channel.hype_train.end")]
    public async Task A_fresh_seed_writes_the_row_off_without_a_message(string eventType)
    {
        await using SeedTestDbContext db = SeedTestDbContext.New();

        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();

        PlatformEventResponseDefault row = await db.PlatformEventResponseDefaults.SingleAsync(d =>
            d.EventType == eventType
        );
        row.IsEnabled.Should().BeFalse();
        row.Message.Should().BeNull();
        row.SpeakWithTts.Should().BeFalse();
    }
}
