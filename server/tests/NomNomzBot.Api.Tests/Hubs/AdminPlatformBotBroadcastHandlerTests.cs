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

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves the platform bot page hears when the shared bot connects or disconnects, and ignores channel bots.</summary>
public sealed class AdminPlatformBotBroadcastHandlerTests
{
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
    public async Task Shared_bot_connect_pushes_the_connected_state_and_username()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceivePlatformBotChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminPlatformBotBroadcastHandler(hub).HandleAsync(
            new BotAccountAuthorizedEvent
            {
                BroadcasterId = Guid.Empty,
                BotAccountId = Guid.NewGuid(),
                IdentityType = AuthEnums.BotIdentityType.Shared,
                BotUsername = "nomnomzbot",
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Connected")!.GetValue(payload).Should().Be(true);
        t.GetProperty("BotUsername")!.GetValue(payload).Should().Be("nomnomzbot");
    }

    [Fact]
    public async Task Shared_bot_disconnect_pushes_the_disconnected_state()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();
        object? payload = null;
        all.ReceivePlatformBotChanged(Arg.Do<object>(p => payload = p)).Returns(Task.CompletedTask);

        await new AdminPlatformBotBroadcastHandler(hub).HandleAsync(
            new BotAccountDisconnectedEvent
            {
                BroadcasterId = Guid.Empty,
                BotAccountId = Guid.NewGuid(),
                Reason = "bot_disconnect",
            }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Connected")!.GetValue(payload).Should().Be(false);
    }

    [Fact]
    public async Task A_channel_bot_connect_pushes_nothing()
    {
        (IHubContext<AdminHub, IAdminClient> hub, IAdminClient all) = HubWithRecorder();

        await new AdminPlatformBotBroadcastHandler(hub).HandleAsync(
            new BotAccountAuthorizedEvent
            {
                BroadcasterId = Guid.NewGuid(),
                BotAccountId = Guid.NewGuid(),
                IdentityType = AuthEnums.BotIdentityType.Custom,
                BotUsername = "channelbot",
            }
        );
        await new AdminPlatformBotBroadcastHandler(hub).HandleAsync(
            new BotAccountDisconnectedEvent
            {
                BroadcasterId = Guid.NewGuid(),
                BotAccountId = Guid.NewGuid(),
                Reason = "channel_bot_disconnect",
            }
        );

        await all.DidNotReceive().ReceivePlatformBotChanged(Arg.Any<object>());
    }
}
