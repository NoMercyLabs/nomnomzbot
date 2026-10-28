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
