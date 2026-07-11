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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the dashboard push classes (BUILD item 5): a plain <c>JoinChannel</c> subscribes every class
/// (the universal shell), <c>JoinChannelClasses</c> subscribes a subset (a chat-only pane never receives
/// live-ops/music traffic) and rejects unknown keys honestly, leaving/rejoining reconciles exactly the
/// joined groups — and the notifier routes each push to its class group while the core pushes
/// (stream status / config / permission / reward invalidations / alerts) stay always-on in the base group.
/// </summary>
public sealed class DashboardEventClassTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    // ── Hub: class-aware joins ──────────────────────────────────────────────

    private static (DashboardHub Hub, IGroupManager Groups) BuildHub(string connectionId)
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(Caller.ToString());
        context.ConnectionId.Returns(connectionId);

        IGroupManager groups = Substitute.For<IGroupManager>();
        DashboardHub hub = new(
            Substitute.For<IChannelRegistry>(),
            NullLogger<DashboardHub>.Instance,
            Substitute.For<IChatProvider>(),
            access,
            Substitute.For<IActionAuthorizationService>(),
            Substitute.For<IOperatorChatSender>()
        )
        {
            Context = context,
            Groups = groups,
        };
        return (hub, groups);
    }

    [Fact]
    public async Task JoinChannel_subscribes_the_base_group_and_every_class()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub("conn-all-classes");

        JoinChannelResponse response = await hub.JoinChannel(Channel.ToString());

        response.Success.Should().BeTrue();
        await groups
            .Received(1)
            .AddToGroupAsync(
                "conn-all-classes",
                $"channel-{Channel}",
                Arg.Any<CancellationToken>()
            );
        foreach (string eventClass in DashboardEventClasses.All)
            await groups
                .Received(1)
                .AddToGroupAsync(
                    "conn-all-classes",
                    $"channel-{Channel}:{eventClass}",
                    Arg.Any<CancellationToken>()
                );
    }

    [Fact]
    public async Task JoinChannelClasses_subscribes_only_the_requested_classes_plus_core()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub("conn-chat-only");

        JoinChannelResponse response = await hub.JoinChannelClasses(Channel.ToString(), ["chat"]);

        response.Success.Should().BeTrue();
        await groups
            .Received(1)
            .AddToGroupAsync("conn-chat-only", $"channel-{Channel}", Arg.Any<CancellationToken>());
        await groups
            .Received(1)
            .AddToGroupAsync(
                "conn-chat-only",
                $"channel-{Channel}:chat",
                Arg.Any<CancellationToken>()
            );
        await groups
            .DidNotReceive()
            .AddToGroupAsync(
                "conn-chat-only",
                $"channel-{Channel}:liveops",
                Arg.Any<CancellationToken>()
            );
        await groups
            .DidNotReceive()
            .AddToGroupAsync(
                "conn-chat-only",
                $"channel-{Channel}:music",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task JoinChannelClasses_rejects_an_unknown_class_without_joining_anything()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub("conn-bad-class");

        JoinChannelResponse response = await hub.JoinChannelClasses(
            Channel.ToString(),
            ["chat", "nonsense"]
        );

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("nonsense");
        await groups
            .DidNotReceiveWithAnyArgs()
            .AddToGroupAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaving_a_class_scoped_channel_removes_exactly_the_joined_groups()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub("conn-leave-classes");
        await hub.JoinChannelClasses(Channel.ToString(), ["chat", "moderation"]);

        await hub.LeaveChannel(Channel.ToString());

        await groups
            .Received(1)
            .RemoveFromGroupAsync(
                "conn-leave-classes",
                $"channel-{Channel}",
                Arg.Any<CancellationToken>()
            );
        await groups
            .Received(1)
            .RemoveFromGroupAsync(
                "conn-leave-classes",
                $"channel-{Channel}:chat",
                Arg.Any<CancellationToken>()
            );
        await groups
            .Received(1)
            .RemoveFromGroupAsync(
                "conn-leave-classes",
                $"channel-{Channel}:moderation",
                Arg.Any<CancellationToken>()
            );
        await groups
            .DidNotReceive()
            .RemoveFromGroupAsync(
                "conn-leave-classes",
                $"channel-{Channel}:music",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Rejoining_with_a_different_class_set_drops_the_stale_class_groups()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub("conn-rejoin");
        await hub.JoinChannelClasses(Channel.ToString(), ["chat", "music"]);

        await hub.JoinChannelClasses(Channel.ToString(), ["chat"]);

        // music was subscribed before but not after — the re-join reconciles it away.
        await groups
            .Received(1)
            .RemoveFromGroupAsync(
                "conn-rejoin",
                $"channel-{Channel}:music",
                Arg.Any<CancellationToken>()
            );
    }

    // ── Notifier: class routing ─────────────────────────────────────────────

    private static (
        DashboardNotifier Notifier,
        IHubClients<IDashboardClient> Clients
    ) BuildNotifier()
    {
        IHubContext<DashboardHub, IDashboardClient> hub = Substitute.For<
            IHubContext<DashboardHub, IDashboardClient>
        >();
        IHubClients<IDashboardClient> clients = Substitute.For<IHubClients<IDashboardClient>>();
        clients.Group(Arg.Any<string>()).Returns(Substitute.For<IDashboardClient>());
        hub.Clients.Returns(clients);
        return (new DashboardNotifier(hub, TimeProvider.System), clients);
    }

    [Fact]
    public async Task Chat_music_and_moderation_pushes_route_to_their_class_groups()
    {
        (DashboardNotifier notifier, IHubClients<IDashboardClient> clients) = BuildNotifier();
        string id = Channel.ToString();

        await notifier.SendChatMessageAsync(
            id,
            new DashboardChatMessageDto(
                "m1",
                id,
                "u1",
                "D",
                "u",
                "hi",
                [],
                "viewer",
                false,
                false,
                false,
                false,
                false,
                false,
                [],
                0,
                null,
                "text",
                null,
                null,
                null,
                "now"
            )
        );
        await notifier.SendMusicStateAsync(id, new MusicStateDto(false, null));
        await notifier.SendModActionAsync(id, new ModActionDto("timeout", "mod-1", "u1", null, 10));

        clients.Received(1).Group($"channel-{id}:chat");
        clients.Received(1).Group($"channel-{id}:music");
        clients.Received(1).Group($"channel-{id}:moderation");
    }

    [Fact]
    public async Task Channel_events_route_by_method_liveops_moderation_and_activity()
    {
        (DashboardNotifier notifier, IHubClients<IDashboardClient> clients) = BuildNotifier();
        string id = Channel.ToString();

        await notifier.NotifyChannelAsync(id, "poll_begin", new { });
        await notifier.NotifyChannelAsync(id, "message_deleted", new { });
        await notifier.NotifyChannelAsync(id, "follow", new { });

        clients.Received(1).Group($"channel-{id}:liveops");
        clients.Received(1).Group($"channel-{id}:moderation");
        clients.Received(1).Group($"channel-{id}:activity");
    }

    [Fact]
    public async Task Core_pushes_stay_always_on_in_the_base_group()
    {
        (DashboardNotifier notifier, IHubClients<IDashboardClient> clients) = BuildNotifier();
        string id = Channel.ToString();

        await notifier.SendStreamStatusAsync(id, new StreamStatusDto(true, null, null, null, null));
        await notifier.SendConfigChangedAsync(
            id,
            new ConfigChangedDto(id, "commands", null, "updated")
        );

        clients.Received(2).Group($"channel-{id}");
    }
}
