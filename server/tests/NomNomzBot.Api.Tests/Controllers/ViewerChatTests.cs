// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Viewer chat (chat-client.md §9a): a viewer reads live chat and sends as themselves on their own token; the
/// moderator who removed a line is never shown to them; the owner's raised key refuses them.
/// </summary>
public sealed class ViewerChatTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-000000000f02");

    // ── Send ──────────────────────────────────────────────────────────────────

    private static ViewerChatController BuildController(
        ChatControllerTestDbContext db,
        IOperatorChatSender sender
    )
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Viewer.ToString());
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });

        return new(
            db,
            decorator,
            Substitute.For<IHubUserEnricher>(),
            currentUser,
            sender,
            Substitute.For<IChatEmoteCatalogue>()
        );
    }

    [Fact]
    public async Task A_viewer_sends_as_themselves_with_their_reply_target()
    {
        IOperatorChatSender sender = Substitute.For<IOperatorChatSender>();
        sender
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ViewerChatController controller = BuildController(
            ChatControllerTestDbContext.New(),
            sender
        );

        IActionResult result = await controller.SendMessage(
            Channel.ToString(),
            new("  hi chat  ", "parent-1")
        );

        result.Should().BeOfType<OkObjectResult>();
        // The caller's OWN user id goes to the Helix send (their token, sender_id = them).
        await sender
            .Received(1)
            .SendAsUserAsync(Viewer, Channel, "hi chat", "parent-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_viewer_controller_has_no_way_to_reach_the_bot_voice()
    {
        ConstructorInfo ctor = typeof(ViewerChatController).GetConstructors().Single();
        Type[] dependencies = [.. ctor.GetParameters().Select(p => p.ParameterType)];

        dependencies.Should().NotContain(typeof(IChatProvider));
        typeof(ViewerChatController.ViewerChatSendRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(["Message", "ReplyToMessageId"]);
    }

    [Fact]
    public async Task A_twitch_refusal_is_returned_not_reported_as_sent()
    {
        IOperatorChatSender sender = Substitute.For<IOperatorChatSender>();
        sender
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("You are banned.", TwitchErrorCodes.NoToken));
        ViewerChatController controller = BuildController(
            ChatControllerTestDbContext.New(),
            sender
        );

        IActionResult result = await controller.SendMessage(Channel.ToString(), new("hi"));

        result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recent_chat_leaves_out_deleted_lines_and_is_oldest_first()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        db.ChatMessages.AddRange(
            Row("m1", 0, deleted: false),
            Row("m2", 1, deleted: true),
            Row("m3", 2, deleted: false)
        );
        await db.SaveChangesAsync();
        ViewerChatController controller = BuildController(
            db,
            Substitute.For<IOperatorChatSender>()
        );

        IActionResult result = await controller.GetMessages(Channel.ToString());

        StatusResponseDto<List<DashboardChatMessageDto>> body =
            (StatusResponseDto<List<DashboardChatMessageDto>>)((OkObjectResult)result).Value!;
        body.Data!.Select(m => m.Id).Should().Equal("m1", "m3");
    }

    private static Domain.Chat.Entities.ChatMessage Row(string id, int second, bool deleted) =>
        new()
        {
            Id = id,
            BroadcasterId = Channel,
            UserId = "u1",
            Username = "viewer1",
            DisplayName = "Viewer1",
            UserType = "viewer",
            Message = id,
            Fragments = [],
            Badges = [],
            CreatedAt = new(2026, 10, 16, 12, 0, second, DateTimeKind.Utc),
            DeletedAt = deleted ? new DateTime(2026, 10, 16, 12, 1, 0, DateTimeKind.Utc) : null,
        };

    // ── Gates on the endpoints ────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(ViewerChatController.GetMessages), "chat:viewer:read")]
    [InlineData(nameof(ViewerChatController.GetEmotes), "chat:viewer:read")]
    [InlineData(nameof(ViewerChatController.SendMessage), "chat:viewer:send")]
    public void Each_endpoint_is_gated_on_its_viewer_key(string method, string actionKey)
    {
        MethodInfo endpoint = typeof(ViewerChatController).GetMethod(method)!;

        endpoint.GetCustomAttribute<RequireActionAttribute>()!.ActionKey.Should().Be(actionKey);
    }

    [Fact]
    public void Send_uses_the_twitch_sized_rate_limit()
    {
        MethodInfo endpoint = typeof(ViewerChatController).GetMethod(
            nameof(ViewerChatController.SendMessage)
        )!;

        endpoint
            .GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should()
            .Be(RateLimitPolicyNames.ViewerChatSend);
        ViewerChatSendRateLimitPolicy.PermitLimit.Should().Be(20);
        ViewerChatSendRateLimitPolicy.Window.Should().Be(TimeSpan.FromSeconds(30));
    }

    // ── Live feed ─────────────────────────────────────────────────────────────

    private static (DashboardHub Hub, IGroupManager Groups) BuildHub(bool viewerChatAllowed)
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .CanResolveTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        // A roleless viewer: only chat:viewer:read may be granted, and only while the owner leaves it open.
        IActionAuthorizationService authorization = Substitute.For<IActionAuthorizationService>();
        authorization
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        authorization
            .AuthorizeActionAsync(Viewer, Channel, "chat:viewer:read", Arg.Any<CancellationToken>())
            .Returns(Result.Success(viewerChatAllowed));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.UserIdentifier.Returns(Viewer.ToString());
        context.ConnectionId.Returns("conn-viewer-chat");
        IGroupManager groups = Substitute.For<IGroupManager>();
        DashboardHub hub = new(
            Substitute.For<IChannelRegistry>(),
            NullLogger<DashboardHub>.Instance,
            Substitute.For<IChatProvider>(),
            access,
            authorization,
            Substitute.For<IOperatorChatSender>()
        )
        {
            Context = context,
            Groups = groups,
        };
        return (hub, groups);
    }

    [Fact]
    public async Task A_viewer_joins_the_viewer_chat_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub(viewerChatAllowed: true);

        JoinChannelResponse response = await hub.JoinViewerChat(Channel.ToString());

        response.Success.Should().BeTrue();
        await groups
            .Received(1)
            .AddToGroupAsync(
                "conn-viewer-chat",
                $"viewer-chat:{Channel}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_owner_switch_off_refuses_the_viewer_chat_group()
    {
        (DashboardHub hub, IGroupManager groups) = BuildHub(viewerChatAllowed: false);

        JoinChannelResponse response = await hub.JoinViewerChat(Channel.ToString());

        response.Success.Should().BeFalse();
        await groups
            .DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static (
        DashboardNotifier Notifier,
        IDashboardClient ModerationClient,
        IDashboardClient ViewerClient
    ) BuildNotifier()
    {
        IHubContext<DashboardHub, IDashboardClient> hub = Substitute.For<
            IHubContext<DashboardHub, IDashboardClient>
        >();
        IHubClients<IDashboardClient> clients = Substitute.For<IHubClients<IDashboardClient>>();
        IDashboardClient moderationClient = Substitute.For<IDashboardClient>();
        IDashboardClient viewerClient = Substitute.For<IDashboardClient>();
        clients.Group(Arg.Any<string>()).Returns(Substitute.For<IDashboardClient>());
        clients.Group($"channel-{Channel}:moderation").Returns(moderationClient);
        clients.Group($"viewer-chat:{Channel}").Returns(viewerClient);
        hub.Clients.Returns(clients);
        return (new(hub, TimeProvider.System), moderationClient, viewerClient);
    }

    [Fact]
    public async Task A_deleted_line_reaches_the_viewer_without_the_moderator()
    {
        (DashboardNotifier notifier, IDashboardClient moderation, IDashboardClient viewer) =
            BuildNotifier();
        MessageDeletedDto named = new("msg-9", "mod-id", "target-id", "Ana");

        await notifier.NotifyChannelAsync(Channel.ToString(), "message_deleted", named);

        await moderation
            .Received(1)
            .ChannelEvent(Arg.Is<ChannelEventDto>(e => ReferenceEquals(e.Data, named)));
        await viewer
            .Received(1)
            .ChannelEvent(
                Arg.Is<ChannelEventDto>(e =>
                    e.Type == "message_deleted" && IsAnonymousDeletion(e.Data)
                )
            );
    }

    [Fact]
    public async Task A_chat_clear_reaches_the_viewer_without_who_cleared_it()
    {
        (DashboardNotifier notifier, _, IDashboardClient viewer) = BuildNotifier();

        await notifier.NotifyChannelAsync(
            Channel.ToString(),
            "chat_cleared",
            new ChatClearedDto("mod-id")
        );

        await viewer
            .Received(1)
            .ChannelEvent(Arg.Is<ChannelEventDto>(e => IsAnonymousClear(e.Data)));
    }

    private static bool IsAnonymousDeletion(object? data) =>
        data is MessageDeletedDto d
        && d.MessageId == "msg-9"
        && d.TargetUserId == "target-id"
        && d.DeletedByUserId == string.Empty
        && d.DeletedByDisplayName == string.Empty;

    private static bool IsAnonymousClear(object? data) =>
        data is ChatClearedDto c && c.ClearedByUserId == string.Empty;

    [Fact]
    public async Task Moderation_only_events_never_reach_the_viewer_group()
    {
        (DashboardNotifier notifier, _, IDashboardClient viewer) = BuildNotifier();

        await notifier.NotifyChannelAsync(Channel.ToString(), "shield_mode_begin", new { });

        await viewer.DidNotReceive().ChannelEvent(Arg.Any<ChannelEventDto>());
    }

    [Fact]
    public async Task A_live_chat_line_reaches_the_viewer_group()
    {
        (DashboardNotifier notifier, _, IDashboardClient viewer) = BuildNotifier();
        DashboardChatMessageDto line = new(
            "m1",
            Channel.ToString(),
            "u1",
            "Viewer1",
            "viewer1",
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
            "2026-10-16T12:00:00Z"
        );

        await notifier.SendChatMessageAsync(Channel.ToString(), line);

        await viewer.Received(1).ChatMessage(line);
    }
}
