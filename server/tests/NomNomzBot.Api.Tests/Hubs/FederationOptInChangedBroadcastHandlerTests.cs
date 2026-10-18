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
