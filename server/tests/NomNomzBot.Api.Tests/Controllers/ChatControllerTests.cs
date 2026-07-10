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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
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
    private static readonly Guid OperatorUserId = Guid.CreateVersion7();

    private static ICurrentUserService StubCurrentUser(Guid? userId)
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId?.ToString());
        return currentUser;
    }

    private static ChatController Build(
        ChatControllerTestDbContext db,
        IChatMessageDecorator decorator,
        IChatProvider? chat = null,
        IOperatorChatSender? operatorSender = null,
        IOperatorMessageDeleter? operatorDeleter = null,
        ICurrentUserService? currentUser = null,
        IChatEmoteCatalogue? emoteCatalogue = null,
        IHubUserEnricher? enricher = null
    ) =>
        new(
            db,
            chat ?? Substitute.For<IChatProvider>(),
            Substitute.For<ITwitchChatApi>(),
            decorator,
            currentUser ?? StubCurrentUser(OperatorUserId),
            operatorSender ?? Substitute.For<IOperatorChatSender>(),
            operatorDeleter ?? Substitute.For<IOperatorMessageDeleter>(),
            emoteCatalogue ?? Substitute.For<IChatEmoteCatalogue>(),
            enricher ?? Substitute.For<IHubUserEnricher>()
        );

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

        List<DashboardChatMessageDto> messages = Data(result);
        messages.Should().ContainSingle();
        DashboardChatMessageDto message = messages[0];

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

        List<DashboardChatMessageDto> messages = Data(result);
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

    [Fact]
    public async Task GetMessages_enriches_history_with_pronouns_avatar_role_and_the_rows_real_timestamp()
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
        DateTime created = new(2026, 7, 2, 9, 30, 15, DateTimeKind.Utc);
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = "msg-1",
                BroadcasterId = Broadcaster,
                UserId = "twitch-42",
                Username = "viewer1",
                DisplayName = "Viewer1",
                UserType = "vip",
                Message = "hi",
                Fragments = [new ChatMessageFragment { Type = "text", Text = "hi" }],
                Badges = [],
                CreatedAt = created,
            }
        );
        await db.SaveChangesAsync();

        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });

        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        enricher
            .EnrichAsync(Broadcaster, "twitch-42", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Viewer1", "https://cdn/avatar.png", "She/Her", "Vip"));

        ChatController controller = Build(db, decorator, enricher: enricher);

        DashboardChatMessageDto message = Data(await controller.GetMessages(Broadcaster.ToString()))
            .Should()
            .ContainSingle()
            .Subject;

        // History carries the SAME enriched shape as the live hub: pronouns + avatar + role flags + real ts.
        message.Pronouns.Should().Be("She/Her");
        message.AvatarUrl.Should().Be("https://cdn/avatar.png");
        message.IsVip.Should().BeTrue();
        message.Timestamp.Should().Be(created.ToString("O"));
    }

    [Fact]
    public async Task SendMessage_as_bot_returns_ok_when_the_bot_delivers_the_message_to_twitch()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        ChatController controller = Build(db, Substitute.For<IChatMessageDecorator>(), chat: chat);

        IActionResult result = await controller.SendMessage(
            Broadcaster.ToString(),
            new ChatController.SendChatMessageRequest("hello chat", SenderIdentity: "bot")
        );

        result.Should().BeOfType<OkObjectResult>();
        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "hello chat", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_as_bot_returns_503_when_the_send_fails_instead_of_reporting_a_false_success()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider chat = Substitute.For<IChatProvider>();
        // The provider swallowed a Helix rejection (e.g. a dead token) and returned false — the controller must
        // NOT pretend the send worked (the old {data:true} lie); it reports 503 so the chat page shows the failure.
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        ChatController controller = Build(db, Substitute.For<IChatMessageDecorator>(), chat: chat);

        IActionResult result = await controller.SendMessage(
            Broadcaster.ToString(),
            new ChatController.SendChatMessageRequest("hello chat", SenderIdentity: "bot")
        );

        ObjectResult response = result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task SendMessage_defaults_to_sending_as_the_logged_in_operator_not_the_bot()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider bot = Substitute.For<IChatProvider>();
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
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            chat: bot,
            operatorSender: operatorSender,
            currentUser: StubCurrentUser(OperatorUserId)
        );

        IActionResult result = await controller.SendMessage(
            Broadcaster.ToString(),
            new ChatController.SendChatMessageRequest("hey there", ReplyToMessageId: "parent-99")
        );

        result.Should().BeOfType<OkObjectResult>();
        // Sent as the OPERATOR (their own account) with the caller's user id + reply parent — never the bot.
        await operatorSender
            .Received(1)
            .SendAsUserAsync(
                OperatorUserId,
                Broadcaster,
                "hey there",
                "parent-99",
                Arg.Any<CancellationToken>()
            );
        await bot.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_as_operator_surfaces_the_twitch_error_instead_of_a_false_success()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IOperatorChatSender operatorSender = Substitute.For<IOperatorChatSender>();
        // The operator's own send was rejected (dead token, or they are banned/timed-out in this channel). The
        // controller must surface that honestly, never a {data:true} that lies the send landed.
        operatorSender
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "Your Twitch connection needs reconnecting.",
                    TwitchErrorCodes.NoToken
                )
            );
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            operatorSender: operatorSender
        );

        IActionResult result = await controller.SendMessage(
            Broadcaster.ToString(),
            new ChatController.SendChatMessageRequest("hey there")
        );

        result.Should().NotBeOfType<OkObjectResult>();
        ObjectResult response = result.Should().BeAssignableTo<ObjectResult>().Subject;
        response.StatusCode.Should().Be(409); // TwitchErrorCodes.NoToken → Conflict
    }

    [Fact]
    public async Task SendMessage_as_operator_returns_401_when_there_is_no_authenticated_user()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IOperatorChatSender operatorSender = Substitute.For<IOperatorChatSender>();
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            operatorSender: operatorSender,
            currentUser: StubCurrentUser(null)
        );

        IActionResult result = await controller.SendMessage(
            Broadcaster.ToString(),
            new ChatController.SendChatMessageRequest("hey there")
        );

        ObjectResult response = result.Should().BeAssignableTo<ObjectResult>().Subject;
        response.StatusCode.Should().Be(401);
        await operatorSender
            .DidNotReceive()
            .SendAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeleteMessage_deletes_as_the_logged_in_operator_never_via_the_bots_tenant_path()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider bot = Substitute.For<IChatProvider>();
        IOperatorMessageDeleter deleter = Substitute.For<IOperatorMessageDeleter>();
        deleter
            .DeleteAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            chat: bot,
            operatorDeleter: deleter,
            currentUser: StubCurrentUser(OperatorUserId)
        );

        IActionResult result = await controller.DeleteMessage(Broadcaster.ToString(), "msg-1");

        result.Should().BeOfType<OkObjectResult>();
        // Routed through the OPERATOR deleter with the caller's user id — so Twitch attributes it to them, not the
        // broadcaster — and the bot's tenant delete path is NEVER touched when acting as the operator succeeds.
        await deleter
            .Received(1)
            .DeleteAsUserAsync(OperatorUserId, Broadcaster, "msg-1", Arg.Any<CancellationToken>());
        await bot.DidNotReceive()
            .DeleteMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_falls_back_to_the_tenant_delete_only_when_the_operator_has_no_linked_twitch_identity()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider bot = Substitute.For<IChatProvider>();
        IOperatorMessageDeleter deleter = Substitute.For<IOperatorMessageDeleter>();
        // The one documented edge case: the operator has no linked Twitch identity to act as (no_token). The
        // controller falls back to the tenant delete rather than failing the moderation outright.
        deleter
            .DeleteAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure("You have no linked Twitch identity.", TwitchErrorCodes.NoToken)
            );
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            chat: bot,
            operatorDeleter: deleter
        );

        IActionResult result = await controller.DeleteMessage(Broadcaster.ToString(), "msg-1");

        result.Should().BeOfType<OkObjectResult>();
        await bot.Received(1)
            .DeleteMessageAsync(Broadcaster, "msg-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_surfaces_a_twitch_rejection_and_never_silently_retries_as_the_broadcaster()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider bot = Substitute.For<IChatProvider>();
        IOperatorMessageDeleter deleter = Substitute.For<IOperatorMessageDeleter>();
        // Twitch rejected the operator's own delete (e.g. rate-limited, or they are not actually a mod there). This is
        // NOT the no-token edge case, so the controller must surface it honestly — never fall back to a broadcaster-
        // attributed retry that would both mis-attribute AND lie {data:true}.
        deleter
            .DeleteAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Slow down.", TwitchErrorCodes.RateLimited));
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            chat: bot,
            operatorDeleter: deleter
        );

        IActionResult result = await controller.DeleteMessage(Broadcaster.ToString(), "msg-1");

        ObjectResult response = result.Should().BeAssignableTo<ObjectResult>().Subject;
        response.StatusCode.Should().Be(429); // TwitchErrorCodes.RateLimited → Too Many Requests
        await bot.DidNotReceive()
            .DeleteMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_returns_401_and_deletes_nothing_when_there_is_no_authenticated_user()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatProvider bot = Substitute.For<IChatProvider>();
        IOperatorMessageDeleter deleter = Substitute.For<IOperatorMessageDeleter>();
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            chat: bot,
            operatorDeleter: deleter,
            currentUser: StubCurrentUser(null)
        );

        IActionResult result = await controller.DeleteMessage(Broadcaster.ToString(), "msg-1");

        ObjectResult response = result.Should().BeAssignableTo<ObjectResult>().Subject;
        response.StatusCode.Should().Be(401);
        await deleter
            .DidNotReceive()
            .DeleteAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await bot.DidNotReceive()
            .DeleteMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_rejects_an_invalid_channel_id_without_deleting()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IOperatorMessageDeleter deleter = Substitute.For<IOperatorMessageDeleter>();
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            operatorDeleter: deleter
        );

        IActionResult result = await controller.DeleteMessage("not-a-guid", "msg-1");

        result.Should().BeOfType<BadRequestObjectResult>();
        await deleter
            .DidNotReceive()
            .DeleteAsUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetEmotes_returns_the_channel_catalogue_flattened_for_the_composer()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatEmoteCatalogue catalogue = Substitute.For<IChatEmoteCatalogue>();
        catalogue
            .GetForChannelAsync(Broadcaster, OperatorUserId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ChatEmote>>([
                    new ChatEmote(
                        EmoteProvider.SevenTv,
                        "7tv-1",
                        "catJAM",
                        new Dictionary<string, string> { ["1"] = "https://7tv/catJAM" },
                        Animated: true,
                        ZeroWidth: false
                    ),
                ])
            );
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            emoteCatalogue: catalogue
        );

        IActionResult result = await controller.GetEmotes(Broadcaster.ToString());

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ChatController.ChatEmoteCatalogueDto>> body =
            (StatusResponseDto<IReadOnlyList<ChatController.ChatEmoteCatalogueDto>>)ok.Value!;
        ChatController.ChatEmoteCatalogueDto emote = body.Data!.Should().ContainSingle().Subject;
        emote.Code.Should().Be("catJAM");
        emote.Provider.Should().Be("SevenTv");
        emote.Animated.Should().BeTrue();
        emote.Urls["1"].Should().Be("https://7tv/catJAM");
    }

    [Fact]
    public async Task GetEmotes_rejects_an_invalid_channel_id()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        ChatController controller = Build(db, Substitute.For<IChatMessageDecorator>());

        IActionResult result = await controller.GetEmotes("not-a-guid");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetEmotes_returns_401_when_no_operator_is_authenticated()
    {
        // The catalogue's user-emotes source is the operator's OWN emotes, so an unresolved operator can't build
        // one — reject before touching the catalogue rather than silently keying it to the wrong actor.
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        IChatEmoteCatalogue catalogue = Substitute.For<IChatEmoteCatalogue>();
        ChatController controller = Build(
            db,
            Substitute.For<IChatMessageDecorator>(),
            currentUser: StubCurrentUser(null),
            emoteCatalogue: catalogue
        );

        IActionResult result = await controller.GetEmotes(Broadcaster.ToString());

        ObjectResult response = result.Should().BeAssignableTo<ObjectResult>().Subject;
        response.StatusCode.Should().Be(401);
        await catalogue
            .DidNotReceive()
            .GetForChannelAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static List<DashboardChatMessageDto> Data(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        OkObjectResult ok = (OkObjectResult)result;
        StatusResponseDto<List<DashboardChatMessageDto>> body =
            (StatusResponseDto<List<DashboardChatMessageDto>>)ok.Value!;
        return body.Data!;
    }
}
