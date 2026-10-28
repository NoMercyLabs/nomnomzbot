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
/// An overlay page joining or leaving a widget changes that widget's "loaded in OBS" state, so the channel's
/// dashboards get one <c>widgets</c> change signal each time and refetch the widget list.
/// </summary>
public sealed class WidgetPresenceBroadcastHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000f31");

    [Fact]
    public async Task A_widget_connect_sends_the_channel_one_widgets_change_for_that_widget()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid widget = Guid.CreateVersion7();
        WidgetConnectedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                WidgetId = widget,
                ConnectionId = "c1",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                Channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == Channel.ToString()
                    && dto.Domain == "widgets"
                    && dto.EntityId == widget.ToString()
                    && dto.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_disconnect_sends_the_channel_the_same_widgets_change_again()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid widget = Guid.CreateVersion7();
        WidgetDisconnectedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                WidgetId = widget,
                ConnectionId = "c1",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                Channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.Domain == "widgets"
                    && dto.EntityId == widget.ToString()
                    && dto.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
