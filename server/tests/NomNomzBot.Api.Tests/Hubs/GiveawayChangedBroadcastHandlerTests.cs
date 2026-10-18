// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a giveaway change reaches the channel's dashboard clients live: one <c>GiveawayChanged</c> push per
/// event, naming the giveaway and the change, so the giveaways page reloads with no polling.
/// </summary>
public sealed class GiveawayChangedBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AChange_PushesOneGiveawayChangedToThatChannel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        GiveawayChangedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid giveaway = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                GiveawayId = giveaway,
                Change = "drawn",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "GiveawayChanged",
                Arg.Is<GiveawayChangedAlertDto>(d =>
                    d.GiveawayId == giveaway && d.Change == "drawn"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_APlatformLevelEvent_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        GiveawayChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                GiveawayId = Guid.CreateVersion7(),
                Change = "opened",
            }
        );

        await notifier.DidNotReceive().NotifyChannelAsync(default!, default!, default!);
    }
}
