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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The join hands the page its effective settings (the saved bag overlaid on the defaults the widget declares),
/// never the raw saved bag: a widget the streamer never configured must still see every declared default.
/// </summary>
public sealed class OverlayHubJoinSettingsTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f22");

    [Fact]
    public async Task Joining_a_widget_returns_the_effective_settings_not_the_raw_saved_bag()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid widgetId = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = widgetId,
                BroadcasterId = Broadcaster,
                Name = "Defaults",
                IsEnabled = true,
                Settings = new() { ["size"] = 40d },
            }
        );
        await db.SaveChangesAsync();

        IWidgetService widgetService = Substitute.For<IWidgetService>();
        Dictionary<string, object> effective = new() { ["size"] = 40d, ["accent"] = "#ff00aa" };
        widgetService
            .GetEffectiveSettingsAsync(Broadcaster, widgetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(effective));

        OverlayTicketService tickets = new(new FakeTimeProvider());
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("join-conn");
        DefaultHttpContext http = new();
        http.Request.QueryString = new(
            "?ticket="
                + Uri.EscapeDataString(
                    tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
                )
        );
        context.GetHttpContext().Returns(http);
        OverlayHub hub = new(
            db,
            widgetService,
            tickets,
            new(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
        };
        await hub.OnConnectedAsync();

        JoinWidgetResponse join = await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        join.Success.Should().BeTrue();
        join.InitialState.Should().BeSameAs(effective);
        ((Dictionary<string, object>)join.InitialState!)["accent"].Should().Be("#ff00aa");
    }
}
