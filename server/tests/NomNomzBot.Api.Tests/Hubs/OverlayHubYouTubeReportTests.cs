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
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A YouTube player page reports its state through the hub. The report counts only for a widget of the
/// connection's own channel; anything else is refused and leaves the player state store untouched.
/// </summary>
public sealed class OverlayHubYouTubeReportTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000b01");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-000000000b02");

    private sealed record Rig(
        OverlayHub Hub,
        YouTubePlayerStateStore Store,
        WidgetTestDbContext Db,
        Guid OwnWidget,
        Guid ForeignWidget
    );

    private static async Task<Rig> ConnectedAsync()
    {
        WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid own = await SeedWidgetAsync(db, Broadcaster);
        Guid foreign = await SeedWidgetAsync(db, OtherChannel);

        FakeTimeProvider time = new();
        OverlayTicketService tickets = new(time);
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("yt-page");
        DefaultHttpContext http = new();
        http.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
        context.GetHttpContext().Returns(http);
        context.Items.Returns(new Dictionary<object, object?>());
        context.ConnectionAborted.Returns(CancellationToken.None);
        IHubCallerClients<IOverlayClient> clients = Substitute.For<
            IHubCallerClients<IOverlayClient>
        >();
        clients.Caller.Returns(Substitute.For<IOverlayClient>());

        OverlayHub hub = new(
            db,
            Substitute.For<IWidgetService>(),
            tickets,
            new OverlayPresenceRegistry(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = clients,
        };
        await hub.OnConnectedAsync();
        return new(hub, new YouTubePlayerStateStore(time), db, own, foreign);
    }

    private static async Task<Guid> SeedWidgetAsync(WidgetTestDbContext db, Guid broadcasterId)
    {
        WidgetGalleryItem item = new() { Name = "yt", NaturalKey = "yt-" + broadcasterId };
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            Name = "yt",
            IsEnabled = true,
            GalleryItemId = item.Id,
        };
        db.WidgetGalleryItems.Add(item);
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    private static YouTubePlayerReportService ReportService(YouTubePlayerStateStore store) =>
        new(
            store,
            Substitute.For<IYouTubePlayerDispatcher>(),
            Substitute.For<IEventBus>(),
            new PlayOnceResumeTracker()
        );

    [Fact]
    public async Task A_report_for_another_channels_widget_is_refused_and_stores_nothing()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await rig.Hub.ReportYouTubePlayerState(
            rig.ForeignWidget.ToString(),
            "dQw4w9WgXcQ",
            "Playing",
            1_000,
            ReportService(rig.Store)
        );

        accepted.Should().BeFalse();
        rig.Store.GetFresh(Broadcaster).Should().BeNull();
        rig.Store.GetFresh(OtherChannel).Should().BeNull();
    }

    [Fact]
    public async Task A_report_with_a_widget_id_that_is_not_a_guid_is_refused_and_stores_nothing()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await rig.Hub.ReportYouTubePlayerState(
            "not-a-guid",
            "dQw4w9WgXcQ",
            "Playing",
            1_000,
            ReportService(rig.Store)
        );

        accepted.Should().BeFalse();
        rig.Store.GetFresh(Broadcaster).Should().BeNull();
    }

    [Fact]
    public async Task A_report_for_the_own_widget_is_accepted_and_the_store_holds_it()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await rig.Hub.ReportYouTubePlayerState(
            rig.OwnWidget.ToString(),
            "dQw4w9WgXcQ",
            "Playing",
            42_000,
            ReportService(rig.Store)
        );

        accepted.Should().BeTrue();
        YouTubePlayerReport? stored = rig.Store.GetFresh(Broadcaster);
        stored.Should().NotBeNull();
        stored.VideoId.Should().Be("dQw4w9WgXcQ");
        stored.State.Should().Be(YouTubePlayerState.Playing);
        stored.PositionMs.Should().Be(42_000);
        rig.Store.GetFresh(OtherChannel).Should().BeNull();
    }
}
