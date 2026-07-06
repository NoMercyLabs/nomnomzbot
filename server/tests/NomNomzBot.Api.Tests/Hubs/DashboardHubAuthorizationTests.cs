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
/// Proves <see cref="DashboardHub.SendChatMessage"/> enforces the same two gates as the REST send path
/// (ChatController POST messages): Gate 1 entry (<see cref="IChannelAccessService"/>) and Gate 2
/// <c>chat:send</c> (Moderator floor) — a hub cannot carry <c>[RequireAction]</c>, so the gates run in the
/// method body. Denied callers get a failure response and the bot NEVER sends; allowed moderators send.
/// Also proves <see cref="DashboardHub.TriggerAction"/> no longer lies: it reports failure, not fake success.
/// </summary>
public sealed class DashboardHubAuthorizationTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f11");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000f12");

    private sealed record Fixture(
        DashboardHub Hub,
        IChatProvider Chat,
        IOperatorChatSender OperatorSender,
        IActionAuthorizationService Gate2
    );

    private static Fixture Build(bool entryAllowed, bool gate2Allows, bool authenticated = true)
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(entryAllowed);

        IActionAuthorizationService gate2 = Substitute.For<IActionAuthorizationService>();
        gate2
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(gate2Allows));

        IOperatorChatSender operatorSender = Substitute.For<IOperatorChatSender>();
        operatorSender
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(authenticated ? Caller.ToString() : null);
        context.ConnectionId.Returns("test-connection");

        DashboardHub hub = new(
            Substitute.For<IChannelRegistry>(),
            NullLogger<DashboardHub>.Instance,
            chat,
            access,
            gate2,
            operatorSender
        )
        {
            Context = context,
        };
        return new Fixture(hub, chat, operatorSender, gate2);
    }

    [Fact]
    public async Task SendChatMessage_from_a_caller_below_the_chat_send_floor_fails_and_never_sends()
    {
        Fixture f = Build(entryAllowed: true, gate2Allows: false);

        SendMessageResponse response = await f.Hub.SendChatMessage(Channel.ToString(), "hi chat");

        response.Success.Should().BeFalse();
        response.Error.Should().Be("Access denied");
        await f
            .Chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f
            .OperatorSender.DidNotReceive()
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SendChatMessage_denied_at_entry_fails_and_never_consults_gate2()
    {
        Fixture f = Build(entryAllowed: false, gate2Allows: true);

        SendMessageResponse response = await f.Hub.SendChatMessage(Channel.ToString(), "hi chat");

        response.Success.Should().BeFalse();
        await f
            .Gate2.DidNotReceive()
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await f
            .Chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendChatMessage_from_a_moderator_sends_through_the_chat_send_gate()
    {
        Fixture f = Build(entryAllowed: true, gate2Allows: true);

        SendMessageResponse response = await f.Hub.SendChatMessage(Channel.ToString(), "hi chat");

        response.Success.Should().BeTrue();
        await f
            .Gate2.Received(1)
            .AuthorizeActionAsync(Caller, Channel, "chat:send", Arg.Any<CancellationToken>());
        // The default identity is the operator (their own account): the send rides the operator sender with the
        // caller's user id, never the bot provider (chat-client.md §3.1).
        await f
            .OperatorSender.Received(1)
            .SendAsUserAsync(Caller, Channel, "hi chat", null, Arg.Any<CancellationToken>());
        await f
            .Chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendChatMessage_from_an_unauthenticated_connection_fails()
    {
        Fixture f = Build(entryAllowed: true, gate2Allows: true, authenticated: false);

        SendMessageResponse response = await f.Hub.SendChatMessage(Channel.ToString(), "hi chat");

        response.Success.Should().BeFalse();
        await f
            .Chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerAction_reports_failure_instead_of_fake_success()
    {
        Fixture f = Build(entryAllowed: true, gate2Allows: true);

        ActionResponse response = await f.Hub.TriggerAction(Channel.ToString(), "nuke", null);

        response.Success.Should().BeFalse("an unimplemented action must never claim it ran");
        response.Error.Should().NotBeNullOrEmpty();
    }
}
