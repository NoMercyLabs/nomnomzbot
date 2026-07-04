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
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Proves the Charity/Goals EventSub ingest (ROADMAP "Small decided items") is actually wired into the
/// per-channel desired-subscribe set that <see cref="BotLifecycleService.SyncChannelsAsync"/> hands to
/// <c>ITwitchEventSubService.EnsureSubscribedAsync</c> on every channel join/reconcile — translators and
/// domain events for these topics existed already (commit 89d2b82); this is the piece that actually asks
/// Twitch to deliver them. Regresses (deleted/typo'd topic string) fail this test for the right reason: the
/// channel would silently stop receiving that topic even though the translator is fully wired to handle it.
/// </summary>
public sealed class BotLifecycleServiceTopicsTests
{
    private static readonly string[] ExpectedCharityAndGoalTopics =
    [
        "channel.charity_campaign.donate",
        "channel.charity_campaign.start",
        "channel.charity_campaign.progress",
        "channel.charity_campaign.stop",
        "channel.goal.begin",
        "channel.goal.progress",
        "channel.goal.end",
    ];

    [Fact]
    public void ChannelEventTypes_IncludesAllSevenCharityAndGoalTopics()
    {
        BotLifecycleService.ChannelEventTypes.Should().Contain(ExpectedCharityAndGoalTopics);
    }

    [Fact]
    public void ChannelEventTypes_HasNoDuplicateTopics()
    {
        // A duplicate would silently double-subscribe (or mask a copy/paste typo) — the registry's
        // (BroadcasterId, Provider, EventType, Version) unique index would swallow it, but it signals a bug here.
        BotLifecycleService.ChannelEventTypes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ChannelEventTypes_StillIncludesThePreExistingHypeTrainTopics()
    {
        // Regression guard: adding the charity/goal block must not disturb the topics already relied on.
        BotLifecycleService
            .ChannelEventTypes.Should()
            .Contain([
                "channel.hype_train.begin",
                "channel.hype_train.end",
                "channel.chat.message",
            ]);
    }
}
