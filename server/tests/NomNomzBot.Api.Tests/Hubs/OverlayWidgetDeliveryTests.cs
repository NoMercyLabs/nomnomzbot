// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// What each overlay connection actually receives, frame by frame: real <see cref="OverlayHub"/> connects and
/// joins, a real <see cref="WidgetNotifier"/>, and an in-memory group bus standing in for SignalR. A widget gets
/// each event once, as an object, only when it subscribes; a page that joins no widget keeps the generic feed.
/// </summary>
public sealed class OverlayWidgetDeliveryTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000d31");
    private static readonly Guid AlertBoxId = Guid.Parse("0192b000-0000-7000-8000-000000000a01");
    private static readonly Guid ChatBoxId = Guid.Parse("0192b000-0000-7000-8000-000000000a02");
    private static readonly Guid HydrateId = Guid.Parse("0192b000-0000-7000-8000-000000000a03");
    private static readonly Guid ResponderId = Guid.Parse("0192b000-0000-7000-8000-000000000a04");

    private const string FeedPage = "feed-page";
    private const string AlertBoxConn = "alert-box";
    private const string ChatBoxConn = "chat-box";
    private const string HydrateConn = "hydrate";
    private const string ResponderConn = "responder";

    private sealed record Frame(string Target, object? Payload);

    /// <summary>Group membership plus every frame each connection was sent, in order.</summary>
    private sealed class GroupBus : IGroupManager
    {
        private readonly Dictionary<string, HashSet<string>> _members = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Frame>> _frames = new(StringComparer.Ordinal);

        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default
        )
        {
            if (!_members.TryGetValue(groupName, out HashSet<string>? members))
            {
                members = new(StringComparer.Ordinal);
                _members[groupName] = members;
            }
            members.Add(connectionId);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default
        )
        {
            if (_members.TryGetValue(groupName, out HashSet<string>? members))
                members.Remove(connectionId);
            return Task.CompletedTask;
        }

        public IReadOnlyList<Frame> FramesFor(string connectionId) =>
            _frames.TryGetValue(connectionId, out List<Frame>? frames) ? frames : [];

        public Task Deliver(string groupName, string target, object? payload)
        {
            if (!_members.TryGetValue(groupName, out HashSet<string>? members))
                return Task.CompletedTask;
            foreach (string connectionId in members)
            {
                if (!_frames.TryGetValue(connectionId, out List<Frame>? frames))
                {
                    frames = [];
                    _frames[connectionId] = frames;
                }
                frames.Add(new(target, payload));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class GroupClient(GroupBus bus, string groupName) : IOverlayClient
    {
        public Task WidgetEvent(WidgetEventDto evt) =>
            bus.Deliver(groupName, nameof(WidgetEvent), evt);

        public Task WidgetReload() => bus.Deliver(groupName, nameof(WidgetReload), null);

        public Task WidgetSettingsChanged(WidgetSettingsDto settings) =>
            bus.Deliver(groupName, nameof(WidgetSettingsChanged), settings);

        public Task WidgetCompileFailed(WidgetCompileFailedDto error) =>
            bus.Deliver(groupName, nameof(WidgetCompileFailed), error);

        public Task Event(OverlayEventDto evt) => bus.Deliver(groupName, nameof(Event), evt);

        public Task PlaySound(PlaySoundPayload payload) =>
            bus.Deliver(groupName, nameof(PlaySound), payload);

        public Task TtsSpeak(TtsSpeakPayload payload) =>
            bus.Deliver(groupName, nameof(TtsSpeak), payload);

        public Task StopSound(StopSoundPayload payload) =>
            bus.Deliver(groupName, nameof(StopSound), payload);

        public Task TtsQueueControl(TtsQueueControlPayload payload) =>
            bus.Deliver(groupName, nameof(TtsQueueControl), payload);

        public Task Retract(RetractPayload payload) =>
            bus.Deliver(groupName, nameof(Retract), payload);
    }

    private sealed class Stage : IAsyncDisposable
    {
        private readonly OverlayTicketService _tickets = new(new FakeTimeProvider());
        private readonly OverlayPresenceRegistry _presence = new();

        public Stage()
        {
            IHubClients<IOverlayClient> clients = Substitute.For<IHubClients<IOverlayClient>>();
            clients
                .Group(Arg.Any<string>())
                .Returns(call => new GroupClient(Bus, call.Arg<string>()));
            IHubContext<OverlayHub, IOverlayClient> hub = Substitute.For<
                IHubContext<OverlayHub, IOverlayClient>
            >();
            hub.Clients.Returns(clients);
            hub.Groups.Returns(Bus);
            Notifier = new WidgetNotifier(hub, _presence);
        }

        public WidgetTestDbContext Db { get; } = WidgetTestDbContext.New();
        public GroupBus Bus { get; } = new();
        public WidgetNotifier Notifier { get; }

        public async Task SeedWidgetAsync(Guid id, string subscribesTo)
        {
            Db.Widgets.Add(
                new Widget
                {
                    Id = id,
                    BroadcasterId = Broadcaster,
                    Name = subscribesTo,
                    IsEnabled = true,
                    EventSubscriptions = [subscribesTo],
                }
            );
            await Db.SaveChangesAsync();
        }

        /// <summary>Connects one browser source; joins <paramref name="widgetId"/> when given, as the SDK does.</summary>
        public async Task ConnectAsync(
            string connectionId,
            Guid? widgetId,
            bool channelWideTicket = false
        )
        {
            HubCallerContext context = Substitute.For<HubCallerContext>();
            context.ConnectionId.Returns(connectionId);
            context.Items.Returns(new Dictionary<object, object?>());
            DefaultHttpContext http = new();
            string ticket = _tickets.IssueTicket(
                new OverlayTokenScope(Broadcaster, channelWideTicket ? null : widgetId)
            );
            http.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
            context.GetHttpContext().Returns(http);

            OverlayHub hub = new(
                Db,
                Substitute.For<IWidgetService>(),
                _tickets,
                _presence,
                Substitute.For<IChannelRegistry>(),
                NullLogger<OverlayHub>.Instance
            )
            {
                Context = context,
                Groups = Bus,
            };
            await hub.OnConnectedAsync();
            if (widgetId is { } id)
                (await hub.JoinWidgetWithSdk(id.ToString(), "test")).Success.Should().BeTrue();
        }

        public Task FollowAsync(object decorated) =>
            OverlayAlertBroadcast.ToOverlaysAsync(
                Db,
                Notifier,
                Substitute.For<IAlertQueueService>(),
                Substitute.For<IWidgetService>(),
                Broadcaster,
                "twitch",
                "follow",
                decorated,
                channelEventId: null,
                CancellationToken.None
            );

        public OverlayEventFeedAdapter FeedAdapter() => new(Notifier, Db);

        public EventResponseOverlayNotifierAdapter EventResponseAdapter() => new(Notifier, Db);

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private static async Task<Stage> StageAsync()
    {
        Stage stage = new();
        await stage.SeedWidgetAsync(AlertBoxId, "follow");
        await stage.SeedWidgetAsync(ChatBoxId, "ChatMessage");
        await stage.SeedWidgetAsync(HydrateId, "custom.hydrate");
        await stage.SeedWidgetAsync(ResponderId, "event_response");
        await stage.ConnectAsync(FeedPage, widgetId: null);
        await stage.ConnectAsync(AlertBoxConn, AlertBoxId);
        await stage.ConnectAsync(ChatBoxConn, ChatBoxId);
        await stage.ConnectAsync(HydrateConn, HydrateId);
        await stage.ConnectAsync(ResponderConn, ResponderId);
        return stage;
    }

    [Fact]
    public async Task A_widget_that_does_not_subscribe_to_an_alert_gets_no_copy_of_it()
    {
        await using Stage stage = await StageAsync();

        await stage.FollowAsync(new { user = "PogChamp42", followedAt = "2026-10-03T12:00:00Z" });

        stage.Bus.FramesFor(ChatBoxConn).Should().BeEmpty();
        stage.Bus.FramesFor(HydrateConn).Should().BeEmpty();
    }

    [Fact]
    public async Task A_subscribed_widget_gets_each_alert_once_as_an_object_WidgetEvent()
    {
        await using Stage stage = await StageAsync();
        object decorated = new { user = "PogChamp42", followedAt = "2026-10-03T12:00:00Z" };

        await stage.FollowAsync(decorated);

        Frame frame = stage.Bus.FramesFor(AlertBoxConn).Should().ContainSingle().Subject;
        frame.Target.Should().Be(nameof(IOverlayClient.WidgetEvent));
        WidgetEventDto dto = frame.Payload.Should().BeOfType<WidgetEventDto>().Subject;
        dto.WidgetId.Should().Be(AlertBoxId.ToString());
        dto.EventType.Should().Be("follow");
        dto.Data.Should().BeSameAs(decorated);
    }

    [Fact]
    public async Task A_channel_wide_page_that_joins_a_widget_leaves_the_feed()
    {
        await using Stage stage = await StageAsync();
        const string channelWideAlertBox = "channel-wide-alert-box";
        await stage.ConnectAsync(channelWideAlertBox, AlertBoxId, channelWideTicket: true);

        await stage.FollowAsync(new { user = "PogChamp42", followedAt = "2026-10-03T12:00:00Z" });

        Frame frame = stage.Bus.FramesFor(channelWideAlertBox).Should().ContainSingle().Subject;
        frame.Target.Should().Be(nameof(IOverlayClient.WidgetEvent));
        ((WidgetEventDto)frame.Payload!).EventType.Should().Be("follow");
    }

    [Fact]
    public async Task A_page_that_joins_no_widget_keeps_the_generic_feed()
    {
        await using Stage stage = await StageAsync();

        await stage.FollowAsync(new { user = "PogChamp42", followedAt = "2026-10-03T12:00:00Z" });
        await stage
            .FeedAdapter()
            .BroadcastEventAsync(Broadcaster, "custom.hydrate", HydratePayload);

        IReadOnlyList<Frame> frames = stage.Bus.FramesFor(FeedPage);
        frames.Should().HaveCount(2);
        frames.Should().AllSatisfy(f => f.Target.Should().Be(nameof(IOverlayClient.Event)));
        OverlayEventDto follow = (OverlayEventDto)frames[0].Payload!;
        follow.Type.Should().Be("follow");
        JsonDocument
            .Parse(follow.Payload)
            .RootElement.GetProperty("user")
            .GetString()
            .Should()
            .Be("PogChamp42");
        OverlayEventDto hydrate = (OverlayEventDto)frames[1].Payload!;
        hydrate.Type.Should().Be("custom.hydrate");
        hydrate.Payload.Should().Be(HydratePayload);
    }

    [Fact]
    public async Task A_journaled_event_reaches_its_subscribed_widget_as_an_object_WidgetEvent()
    {
        await using Stage stage = await StageAsync();

        await stage
            .FeedAdapter()
            .BroadcastEventAsync(Broadcaster, "custom.hydrate", HydratePayload);

        Frame frame = stage.Bus.FramesFor(HydrateConn).Should().ContainSingle().Subject;
        frame.Target.Should().Be(nameof(IOverlayClient.WidgetEvent));
        WidgetEventDto dto = frame.Payload.Should().BeOfType<WidgetEventDto>().Subject;
        dto.WidgetId.Should().Be(HydrateId.ToString());
        dto.EventType.Should().Be("custom.hydrate");
        JsonElement data = dto.Data.Should().BeOfType<JsonElement>().Subject;
        data.ValueKind.Should().Be(JsonValueKind.Object);
        data.GetProperty("userName").GetString().Should().Be("Hydrator");
        data.GetProperty("cups").GetInt32().Should().Be(3);
        stage.Bus.FramesFor(AlertBoxConn).Should().BeEmpty();
        stage.Bus.FramesFor(ChatBoxConn).Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_response_reaches_its_subscribed_widget_as_an_object_WidgetEvent()
    {
        await using Stage stage = await StageAsync();

        await stage
            .EventResponseAdapter()
            .NotifyAsync(
                Broadcaster,
                "follow",
                "Thanks for the follow, PogChamp42!",
                new Dictionary<string, string> { ["user"] = "PogChamp42" }
            );

        Frame frame = stage.Bus.FramesFor(ResponderConn).Should().ContainSingle().Subject;
        frame.Target.Should().Be(nameof(IOverlayClient.WidgetEvent));
        WidgetEventDto dto = frame.Payload.Should().BeOfType<WidgetEventDto>().Subject;
        dto.EventType.Should().Be("event_response");
        JsonElement data = dto.Data.Should().BeOfType<JsonElement>().Subject;
        data.GetProperty("eventType").GetString().Should().Be("follow");
        data.GetProperty("message").GetString().Should().Be("Thanks for the follow, PogChamp42!");
        data.GetProperty("metadata").GetProperty("user").GetString().Should().Be("PogChamp42");
        stage.Bus.FramesFor(AlertBoxConn).Should().BeEmpty();
        stage.Bus.FramesFor(FeedPage).Should().ContainSingle().Which.Target.Should().Be("Event");
    }

    private const string HydratePayload = """{"userName":"Hydrator","cups":3}""";
}
