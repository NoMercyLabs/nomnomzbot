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
