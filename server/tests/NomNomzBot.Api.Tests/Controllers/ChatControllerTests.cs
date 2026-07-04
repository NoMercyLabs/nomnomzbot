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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <see cref="ChatController.GetMessages"/> runs the SAME <see cref="IChatMessageDecorator"/> pipeline
/// the live <c>DashboardHub</c> broadcast uses over the persisted (raw, un-enriched) chat page, and maps the
/// result through the shared <see cref="ChatFragmentMapper"/> — closing the audited P0 bug where the REST endpoint
/// returned the raw persisted <c>ChatMessageFragment</c>/<c>ChatBadge</c> shape untouched.
/// </summary>
public sealed class ChatControllerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static ChatController Build(
        ChatControllerTestDbContext db,
        IChatMessageDecorator decorator
    ) => new(db, Substitute.For<IChatProvider>(), Substitute.For<ITwitchChatApi>(), decorator);

    [Fact]
    public async Task GetMessages_returns_the_decorators_output_not_the_raw_persisted_fragment()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = "msg-1",
                BroadcasterId = Broadcaster,
                UserId = "u1",
                Username = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
                UserType = "subscriber",
                Message = "PepeLaugh hi",
                // The RAW, un-enriched persisted shape — no Emote, no badge Urls. If GetMessages ever regresses
                // to returning this untouched, the assertions below (which expect the DECORATOR's output) fail.
                Fragments = [new ChatMessageFragment { Type = "text", Text = "PepeLaugh hi" }],
                Badges = [new ChatBadge("subscriber", "6")],
                CreatedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();

        ChatMessageFragment decoratedFragment = new()
        {
            Type = "emote",
            Text = "PepeLaugh",
            Emote = new ChatEmote(
                EmoteProvider.SevenTv,
                "7tv-1",
                "PepeLaugh",
                new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
                Animated: true,
                ZeroWidth: false
            ),
        };
        ResolvedChatBadge decoratedBadge = new(
            "subscriber",
            "6",
            "6",
            new Dictionary<string, string> { ["4"] = "https://cdn/sub-tier6.png" }
        );
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(
                new DecoratedChatMessage
                {
                    Fragments = [decoratedFragment],
                    Badges = [decoratedBadge],
                }
            );

        ChatController controller = Build(db, decorator);

        IActionResult result = await controller.GetMessages(Broadcaster.ToString());

        List<ChatController.ChatMessageDto> messages = Data(result);
        messages.Should().ContainSingle();
        ChatController.ChatMessageDto message = messages[0];

        // Identical shape to what the hub would produce for the SAME decorated fragment/badge — both paths
        // call the exact same ChatFragmentMapper functions.
        message.Fragments.Should().Equal(ChatFragmentMapper.MapFragment(decoratedFragment));
        message.Badges.Should().Equal(ChatFragmentMapper.MapBadge(decoratedBadge));

        // And explicitly NOT the raw persisted shape (no Emote, no badge Urls).
        message.Fragments[0].Emote.Should().NotBeNull();
        message.Badges[0].Urls.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetMessages_decorates_every_row_with_its_own_reconstructed_event_oldest_first()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.ChatMessages.AddRange(
            new ChatMessage
            {
                Id = "msg-older",
                BroadcasterId = Broadcaster,
                UserId = "u1",
                Username = "viewer1",
                DisplayName = "Viewer1",
                UserType = "viewer",
                Message = "first",
                Fragments = [new ChatMessageFragment { Type = "text", Text = "first" }],
                Badges = [],
                CreatedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            },
            new ChatMessage
            {
                Id = "msg-newer",
                BroadcasterId = Broadcaster,
                UserId = "u2",
                Username = "mod1",
                DisplayName = "Mod1",
                UserType = "moderator",
                Message = "second",
                Fragments = [new ChatMessageFragment { Type = "text", Text = "second" }],
                Badges = [],
                CreatedAt = new DateTime(2026, 7, 1, 12, 0, 1, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();

        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });

        ChatController controller = Build(db, decorator);

        IActionResult result = await controller.GetMessages(Broadcaster.ToString());

        List<ChatController.ChatMessageDto> messages = Data(result);
        messages.Select(m => m.Id).Should().Equal("msg-older", "msg-newer");

        await decorator
            .Received(2)
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
        await decorator
            .Received(1)
            .DecorateAsync(
                Arg.Is<ChatMessageReceivedEvent>(e =>
                    e.MessageId == "msg-older"
                    && e.BroadcasterId == Broadcaster
                    && e.TwitchBroadcasterId == "998877"
                    && e.IsModerator == false
                    && e.IsSubscriber == false
                    && e.IsBroadcaster == false
                    && e.Fragments.Count == 1
                    && e.Fragments[0].Text == "first"
                ),
                Arg.Any<CancellationToken>()
            );
        await decorator
            .Received(1)
            .DecorateAsync(
                Arg.Is<ChatMessageReceivedEvent>(e =>
                    e.MessageId == "msg-newer" && e.IsModerator == true && e.IsSubscriber == false
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetMessages_falls_back_to_an_empty_twitch_broadcaster_id_when_the_channel_row_is_missing()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        // No Channel row seeded at all.
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = "msg-1",
                BroadcasterId = Broadcaster,
                UserId = "u1",
                Username = "viewer1",
                DisplayName = "Viewer1",
                UserType = "viewer",
                Message = "hi",
                Fragments = [new ChatMessageFragment { Type = "text", Text = "hi" }],
                Badges = [],
                CreatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });

        ChatController controller = Build(db, decorator);

        await controller.GetMessages(Broadcaster.ToString());

        await decorator
            .Received(1)
            .DecorateAsync(
                Arg.Is<ChatMessageReceivedEvent>(e => e.TwitchBroadcasterId == string.Empty),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetMessages_rejects_an_invalid_channel_id_without_ever_calling_the_decorator()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        ChatController controller = Build(db, decorator);

        IActionResult result = await controller.GetMessages("not-a-guid");

        result.Should().BeOfType<BadRequestObjectResult>();
        await decorator
            .DidNotReceive()
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
    }

    private static List<ChatController.ChatMessageDto> Data(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        OkObjectResult ok = (OkObjectResult)result;
        StatusResponseDto<List<ChatController.ChatMessageDto>> body =
            (StatusResponseDto<List<ChatController.ChatMessageDto>>)ok.Value!;
        return body.Data!;
    }
}
