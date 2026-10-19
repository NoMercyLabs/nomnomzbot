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
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a recorded supporter event (a tip, a membership) reaches the channel's dashboards as a
/// <c>supporters</c> config change, so the Supporters page refetches without a reload.
/// </summary>
public sealed class SupporterEventBroadcastHandlerTests
{
    [Fact]
    public async Task A_recorded_tip_pushes_a_supporters_change_to_that_channel_with_the_row_id()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        SupporterEventBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                SourceKey = "kofi",
                Kind = "tip",
                SupporterDisplayName = "Alice",
                AmountMinor = 500,
                Currency = "EUR",
                SupporterEventId = eventId,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(change =>
                    change.Domain == "supporters"
                    && change.EntityId == eventId.ToString()
                    && change.Action == "received"
                    && change.BroadcasterId == channel.ToString()
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_event_without_a_channel_pushes_nothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        SupporterEventBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                SourceKey = "kofi",
                Kind = "tip",
                SupporterDisplayName = "Alice",
                SupporterEventId = Guid.CreateVersion7(),
            }
        );

        await notifier.DidNotReceive().SendConfigChangedAsync(default!, default!);
    }
}
