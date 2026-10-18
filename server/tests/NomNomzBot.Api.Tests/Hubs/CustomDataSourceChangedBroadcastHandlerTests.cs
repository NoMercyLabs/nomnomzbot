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
/// Proves a custom data source change reaches the channel's dashboard clients live: one
/// <c>CustomDataSourceChanged</c> push per event, naming the source and the change, so the custom events
/// page reloads with no polling.
/// </summary>
public sealed class CustomDataSourceChangedBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_AChange_PushesOneCustomDataSourceChangedToThatChannel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        CustomDataSourceChangedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                SourceName = "sensor",
                Change = "received",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "CustomDataSourceChanged",
                Arg.Is<CustomDataSourceChangedAlertDto>(d =>
                    d.SourceName == "sensor" && d.Change == "received"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_APlatformLevelEvent_PushesNothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        CustomDataSourceChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                SourceName = "sensor",
                Change = "created",
            }
        );

        await notifier.DidNotReceive().NotifyChannelAsync(default!, default!, default!);
    }
}
