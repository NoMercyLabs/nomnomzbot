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
/// Proves a federation opt-in change reaches the channel's dashboard clients live: one
/// <c>FederationOptInChanged</c> push per event, carrying the new state of the choice.
/// </summary>
public sealed class FederationOptInChangedBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AChange_PushesTheNewStateToThatChannel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        FederationOptInChangedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid peer = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                OptInBroadcasterId = channel,
                PeerId = peer,
                OptInType = "shared_ban_list",
                Direction = "accept",
                IsEnabled = true,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "FederationOptInChanged",
                Arg.Is<FederationOptInChangedAlertDto>(d =>
                    d.PeerId == peer
                    && d.OptInType == "shared_ban_list"
                    && d.Direction == "accept"
                    && d.IsEnabled
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AnEventWithoutAChannel_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        FederationOptInChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OptInBroadcasterId = Guid.Empty,
                OptInType = "shared_ban_list",
                Direction = "both",
                IsEnabled = false,
            }
        );

        await notifier.DidNotReceive().NotifyChannelAsync(default!, default!, default!);
    }
}
