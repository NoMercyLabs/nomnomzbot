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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Chat.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

public sealed class ChatNativeFeedBroadcastHandlerTests
{
    [Fact]
    public async Task Non_moderator_chat_group_never_receives_a_held_line()
    {
        List<string> deliveredGroups = [];
        List<DashboardChatMessageDto> deliveredLines = [];
        IHubContext<DashboardHub, IDashboardClient> hub = Substitute.For<
            IHubContext<DashboardHub, IDashboardClient>
        >();
        IHubClients<IDashboardClient> clients = Substitute.For<IHubClients<IDashboardClient>>();
        IDashboardClient recipient = Substitute.For<IDashboardClient>();
        hub.Clients.Returns(clients);
        clients
            .Group(Arg.Any<string>())
            .Returns(call =>
            {
                deliveredGroups.Add(call.ArgAt<string>(0));
                return recipient;
            });
        recipient
            .ChatMessage(Arg.Any<DashboardChatMessageDto>())
            .Returns(call =>
            {
                deliveredLines.Add(call.ArgAt<DashboardChatMessageDto>(0));
                return Task.CompletedTask;
            });
        DashboardNotifier notifier = new(hub, TimeProvider.System);
        DashboardChatMessageDto held = new(
            "held-1",
            "c1",
            "u1",
            "Viewer",
            "viewer",
            "private",
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
            DateTimeOffset.UtcNow.ToString("O"),
            Kind: "held"
        );

        await notifier.SendHeldChatMessageAsync("c1", held);

        deliveredGroups
            .Should()
            .Equal(DashboardEventClasses.ClassGroup("c1", DashboardEventClasses.Moderation));
        deliveredLines.Should().ContainSingle().Which.Kind.Should().Be("held");
    }

    [Fact]
    public async Task Announcement_pushes_a_typed_colored_chat_line()
    {
        Guid channel = Guid.CreateVersion7();
        List<DashboardChatMessageDto> sent = [];
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        notifier
            .SendChatMessageAsync(
                Arg.Any<string>(),
                Arg.Any<DashboardChatMessageDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                sent.Add(call.ArgAt<DashboardChatMessageDto>(1));
                return Task.CompletedTask;
            });
        ChatNativeFeedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ChatNotificationEvent
            {
                BroadcasterId = channel,
                MessageId = "notice-1",
                ChatterUserId = "u-1",
                ChatterLogin = "viewer",
                ChatterDisplayName = "Viewer",
                IsAnonymous = false,
                NoticeType = "announcement",
                SystemMessage = "Viewer made an announcement",
                MessageText = "Welcome",
                AnnouncementColor = "PURPLE",
            }
        );

        sent.Should().ContainSingle();
        sent[0].Kind.Should().Be("notice");
        sent[0].NoticeType.Should().Be("announcement");
        sent[0].AnnouncementColor.Should().Be("PURPLE");
        sent[0].Message.Should().Be("Welcome");
    }

    [Fact]
    public async Task Held_line_uses_moderator_only_delivery()
    {
        Guid channel = Guid.CreateVersion7();
        List<DashboardChatMessageDto> publicLines = [];
        List<DashboardChatMessageDto> moderatorLines = [];
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        notifier
            .SendChatMessageAsync(
                Arg.Any<string>(),
                Arg.Any<DashboardChatMessageDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                publicLines.Add(call.ArgAt<DashboardChatMessageDto>(1));
                return Task.CompletedTask;
            });
        notifier
            .SendHeldChatMessageAsync(
                Arg.Any<string>(),
                Arg.Any<DashboardChatMessageDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                moderatorLines.Add(call.ArgAt<DashboardChatMessageDto>(1));
                return Task.CompletedTask;
            });
        ChatNativeFeedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ChatUserMessageHeldEvent
            {
                BroadcasterId = channel,
                MessageId = "held-1",
                UserId = "u-1",
                UserLogin = "viewer",
                UserDisplayName = "Viewer",
                Text = "private message",
            }
        );

        publicLines.Should().BeEmpty();
        moderatorLines.Should().ContainSingle();
        moderatorLines[0].Kind.Should().Be("held");
        moderatorLines[0].Message.Should().Be("private message");
    }
}
