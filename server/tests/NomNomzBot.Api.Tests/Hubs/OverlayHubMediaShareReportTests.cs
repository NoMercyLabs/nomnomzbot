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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The media-share page reports its player through the hub. The report reaches the playback service (which
/// marks an ended item played and loads the next one) only from a widget of the connection's own channel
/// that subscribes to <c>media_share_command</c>; anything else is refused and reaches nothing.
/// </summary>
public sealed class OverlayHubMediaShareReportTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-000000000c02");
    private static readonly Guid Request = Guid.Parse("0192b000-0000-7000-8000-000000000c10");

    private sealed record Rig(
        OverlayHub Hub,
        IMediaSharePlaybackService Playback,
        WidgetTestDbContext Db,
        Guid Player,
        Guid QueueOnly,
        Guid Foreign
    );

    private static async Task<Rig> ConnectedAsync()
    {
        WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid player = await SeedWidgetAsync(db, Broadcaster, "media_share", "media_share_command");
        Guid queueOnly = await SeedWidgetAsync(db, Broadcaster, "sr_queue", "sr_queue");
        Guid foreign = await SeedWidgetAsync(db, OtherChannel, "foreign", "media_share_command");

        FakeTimeProvider time = new();
        OverlayTicketService tickets = new(time);
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("media-page");
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
            WidgetServiceStubs.SavedSettingsOnly(),
            tickets,
            new OverlayPresenceRegistry(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            [],
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = clients,
        };
        await hub.OnConnectedAsync();

        IMediaSharePlaybackService playback = Substitute.For<IMediaSharePlaybackService>();
        playback
            .ReportStateAsync(
                Arg.Any<Guid>(),
                Arg.Any<MediaPlayerStateReport>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return new(hub, playback, db, player, queueOnly, foreign);
    }

    private static async Task<Guid> SeedWidgetAsync(
        WidgetTestDbContext db,
        Guid broadcasterId,
        string naturalKey,
        string subscription
    )
    {
        WidgetGalleryItem item = new() { Name = naturalKey, NaturalKey = naturalKey };
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = broadcasterId,
            Name = naturalKey,
            IsEnabled = true,
            GalleryItemId = item.Id,
            EventSubscriptions = [subscription],
        };
        db.WidgetGalleryItems.Add(item);
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    private static Task<bool> ReportAsync(Rig rig, Guid widget, string requestId, string state) =>
        rig.Hub.ReportMediaSharePlayerState(
            widget.ToString(),
            requestId,
            state,
            31,
            213,
            70,
            rig.Playback
        );

    private static async Task ReceivedNothingAsync(Rig rig) =>
        await rig.Playback.DidNotReceiveWithAnyArgs().ReportStateAsync(default, default!, default);

    [Fact]
    public async Task The_own_player_widget_report_reaches_the_playback_service_with_every_field()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await ReportAsync(rig, rig.Player, Request.ToString(), "ended");

        accepted.Should().BeTrue();
        await rig
            .Playback.Received(1)
            .ReportStateAsync(
                Broadcaster,
                Arg.Is<MediaPlayerStateReport>(r =>
                    r.RequestId == Request
                    && r.State == MediaPlayerStates.Ended
                    && r.PositionSeconds == 31
                    && r.DurationSeconds == 213
                    && r.Volume == 70
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_empty_request_id_reaches_the_service_as_no_request()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await ReportAsync(rig, rig.Player, "", "idle");

        accepted.Should().BeTrue();
        await rig
            .Playback.Received(1)
            .ReportStateAsync(
                Broadcaster,
                Arg.Is<MediaPlayerStateReport>(r => r.RequestId == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_without_the_media_share_subscription_is_refused()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await ReportAsync(rig, rig.QueueOnly, Request.ToString(), "ended");

        accepted.Should().BeFalse();
        await ReceivedNothingAsync(rig);
    }

    [Fact]
    public async Task A_widget_of_another_channel_is_refused()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await ReportAsync(rig, rig.Foreign, Request.ToString(), "ended");

        accepted.Should().BeFalse();
        await ReceivedNothingAsync(rig);
    }

    [Fact]
    public async Task An_unknown_state_is_refused()
    {
        Rig rig = await ConnectedAsync();
        using WidgetTestDbContext db = rig.Db;

        bool accepted = await ReportAsync(rig, rig.Player, Request.ToString(), "rewinding");

        accepted.Should().BeFalse();
        await ReceivedNothingAsync(rig);
    }
}
