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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A signed-in viewer with no channel role may join the viewer-safe <c>viewer-music:{channel}</c> group
/// (public queue + now-playing only) while the <c>music</c> class stays refused; the notifier mirrors
/// <c>sr_queue_changed</c> and <c>MusicStateChanged</c> into that group.
/// </summary>
public sealed class ViewerMusicGroupTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-000000000d02");

    private static (DashboardHub Hub, IGroupManager Groups) BuildViewerHub(
        string connectionId,
        bool authenticated = true
    )
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        // The viewer holds no channel role: every Gate-2 action is refused.
        IActionAuthorizationService authorization = Substitute.For<IActionAuthorizationService>();
        authorization
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(authenticated ? Viewer.ToString() : null);
        context.ConnectionId.Returns(connectionId);
        IGroupManager groups = Substitute.For<IGroupManager>();
        DashboardHub hub = new(
            Substitute.For<IChannelRegistry>(),
            NullLogger<DashboardHub>.Instance,
            Substitute.For<IChatProvider>(),
            access,
            authorization,
            Substitute.For<IOperatorChatSender>(),
            Substitute.For<IExternalChatRelay>()
        )
        {
            Context = context,
            Groups = groups,
        };
        return (hub, groups);
    }

    [Fact]
    public async Task A_roleless_viewer_joins_the_viewer_music_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildViewerHub("conn-viewer-music");

        JoinChannelResponse response = await hub.JoinViewerMusic(Channel.ToString());

        response.Success.Should().BeTrue();
        await groups
            .Received(1)
            .AddToGroupAsync(
                "conn-viewer-music",
                $"viewer-music:{Channel}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_same_viewer_still_gets_no_music_class_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildViewerHub("conn-viewer-gate");

        JoinChannelResponse response = await hub.Subscribe(Channel.ToString(), ["music"]);

        response.GrantedClasses.Should().BeEmpty();
        await groups
            .DidNotReceive()
            .AddToGroupAsync(
                "conn-viewer-gate",
                $"channel-{Channel}:music",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unauthenticated_connection_is_refused_the_viewer_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildViewerHub(
            "conn-anon",
            authenticated: false
        );

        JoinChannelResponse response = await hub.JoinViewerMusic(Channel.ToString());

        response.Success.Should().BeFalse();
        await groups
            .DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaving_removes_the_viewer_music_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildViewerHub("conn-viewer-leave");
        await hub.JoinViewerMusic(Channel.ToString());

        await hub.LeaveViewerMusic(Channel.ToString());

        await groups
            .Received(1)
            .RemoveFromGroupAsync(
                "conn-viewer-leave",
                $"viewer-music:{Channel}",
                Arg.Any<CancellationToken>()
            );
    }

    private static (
        DashboardNotifier Notifier,
        IHubClients<IDashboardClient> Clients,
        IDashboardClient ViewerClient
    ) BuildNotifier(string id)
    {
        IHubContext<DashboardHub, IDashboardClient> hub = Substitute.For<
            IHubContext<DashboardHub, IDashboardClient>
        >();
        IHubClients<IDashboardClient> clients = Substitute.For<IHubClients<IDashboardClient>>();
        IDashboardClient viewerClient = Substitute.For<IDashboardClient>();
        clients.Group(Arg.Any<string>()).Returns(Substitute.For<IDashboardClient>());
        clients.Group($"viewer-music:{id}").Returns(viewerClient);
        hub.Clients.Returns(clients);
        return (new(hub, TimeProvider.System), clients, viewerClient);
    }

    [Fact]
    public async Task A_queue_change_is_pushed_to_the_viewer_group_with_the_same_payload()
    {
        string id = Channel.ToString();
        (DashboardNotifier notifier, _, IDashboardClient viewerClient) = BuildNotifier(id);
        object payload = new { items = new[] { new { title = "Song A", requestedBy = "v1" } } };

        await notifier.NotifyChannelAsync(id, "sr_queue_changed", payload);

        await viewerClient
            .Received(1)
            .ChannelEvent(
                Arg.Is<ChannelEventDto>(e =>
                    e.Type == "sr_queue_changed" && ReferenceEquals(e.Data, payload)
                )
            );
    }

    [Fact]
    public async Task Other_channel_events_never_reach_the_viewer_group()
    {
        string id = Channel.ToString();
        (DashboardNotifier notifier, _, IDashboardClient viewerClient) = BuildNotifier(id);

        await notifier.NotifyChannelAsync(id, "follow", new { });

        await viewerClient.DidNotReceive().ChannelEvent(Arg.Any<ChannelEventDto>());
    }

    [Fact]
    public async Task Now_playing_is_pushed_to_the_viewer_group()
    {
        string id = Channel.ToString();
        (DashboardNotifier notifier, _, IDashboardClient viewerClient) = BuildNotifier(id);
        MusicStateDto state = new(true, null);

        await notifier.SendMusicStateAsync(id, state);

        await viewerClient.Received(1).MusicStateChanged(state);
    }
}
