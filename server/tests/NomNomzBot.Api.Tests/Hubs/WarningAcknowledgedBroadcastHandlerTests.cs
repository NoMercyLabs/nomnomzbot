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
/// Proves a warning acknowledgement reaches the channel's dashboards as a <c>moderation-history</c> config change
/// (this event had no consumer before this slice).
/// </summary>
public sealed class WarningAcknowledgedBroadcastHandlerTests
{
    [Fact]
    public async Task An_acknowledgement_pushes_a_moderation_history_change_for_that_channel_and_viewer()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WarningAcknowledgedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "viewer-42",
                UserDisplayName = "Viewer42",
                UserLogin = "viewer42",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(change =>
                    change.Domain == "moderation-history"
                    && change.EntityId == "viewer-42"
                    && change.Action == "warning_acknowledged"
                    && change.BroadcasterId == channel.ToString()
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_platform_sentinel_channel_pushes_nothing()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WarningAcknowledgedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                UserId = "viewer-42",
                UserDisplayName = "Viewer42",
                UserLogin = "viewer42",
            }
        );

        await notifier
            .DidNotReceive()
            .SendConfigChangedAsync(
                Arg.Any<string>(),
                Arg.Any<ConfigChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
