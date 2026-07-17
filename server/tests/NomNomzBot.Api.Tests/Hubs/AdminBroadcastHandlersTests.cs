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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Stream.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the AdminHub is no longer a dead pipe: channel online/offline flips and tenant suspensions each
/// push the operator-facing message with the load-bearing payload fields.
/// </summary>
public sealed class AdminBroadcastHandlersTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000dd01");

    private static (IHubContext<AdminHub, IAdminClient> Hub, IAdminClient All) HubWithRecorder()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        return (hub, all);
    }

    [Fact]
    public async Task Channel_online_pushes_a_live_registry_update()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveChannelRegistryUpdate(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminChannelOnlineBroadcastHandler(hub).HandleAsync(
            new ChannelOnlineEvent
            {
                BroadcasterId = Channel,
                BroadcasterDisplayName = "Stoney",
                StreamTitle = "blame the lag",
                GameName = "Deep Rock",
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        payload.Should().NotBeNull();
        System.Type t = payload!.GetType();
        t.GetProperty("BroadcasterId")!.GetValue(payload).Should().Be(Channel);
        t.GetProperty("IsLive")!.GetValue(payload).Should().Be(true);
        t.GetProperty("ChannelName")!.GetValue(payload).Should().Be("Stoney");
    }

    [Fact]
    public async Task Channel_offline_pushes_a_not_live_registry_update()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceiveChannelRegistryUpdate(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminChannelOfflineBroadcastHandler(hub).HandleAsync(
            new ChannelOfflineEvent
            {
                BroadcasterId = Channel,
                BroadcasterDisplayName = "Stoney",
                StreamDuration = TimeSpan.FromHours(3),
            }
        );

        payload!.GetType().GetProperty("IsLive")!.GetValue(payload).Should().Be(false);
    }

    [Fact]
    public async Task Tenant_suspension_pushes_a_registry_update_and_a_warning_log_line()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? log = null;
        all.ReceiveLog(Arg.Do<object>(p => log = p)).Returns(Task.CompletedTask);

        await new AdminTenantSuspensionBroadcastHandler(hub).HandleAsync(
            new TenantSuspensionChangedEvent
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = Guid.NewGuid(),
                TargetBroadcasterId = Channel,
                NewStatus = "suspended",
                Reason = "ToS",
            }
        );

        await all.Received(1).ReceiveChannelRegistryUpdate(Arg.Any<object>());
        log.Should().NotBeNull();
        log!.GetType().GetProperty("Type")!.GetValue(log).Should().Be("warning");
        log.GetType()
            .GetProperty("Message")!
            .GetValue(log)!
            .ToString()
            .Should()
            .Contain("suspended");
    }
}
