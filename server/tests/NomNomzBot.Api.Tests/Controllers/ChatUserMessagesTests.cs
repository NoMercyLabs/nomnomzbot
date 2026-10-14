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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>GET chat/users/{userId}/messages</c> over real persisted rows: one person's lines in this channel only,
/// newest first, deleted rows and other people and other channels never come back, and the cursor walks to the very
/// first line and then ends.
/// </summary>
public sealed class ChatUserMessagesTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid OtherBroadcaster = Guid.CreateVersion7();
    private static readonly DateTime Day1 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ChatMessage Row(
        string id,
        DateTime createdAt,
        string userId = "u1",
        Guid? broadcaster = null,
        DateTime? deletedAt = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster ?? Broadcaster,
            UserId = userId,
            Username = "viewer1",
            DisplayName = "Viewer1",
            UserType = "viewer",
            Message = "line " + id,
            Fragments = [new() { Type = "text", Text = "line " + id }],
            Badges = [],
            CreatedAt = createdAt,
            DeletedAt = deletedAt,
        };

    private static async Task<ChatControllerTestDbContext> Seed(params ChatMessage[] rows)
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.ChatMessages.AddRange(rows);
        await db.SaveChangesAsync();
        return db;
    }

    private static ChatController Build(ChatControllerTestDbContext db)
    {
        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });

        return new(
            db,
            Substitute.For<IChatProvider>(),
            Substitute.For<ITwitchChatApi>(),
            decorator,
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IOperatorChatSender>(),
            Substitute.For<IOperatorMessageDeleter>(),
            Substitute.For<IChatEmoteCatalogue>(),
            Substitute.For<IHubUserEnricher>(),
            new ChatSearchService(db)
        );
    }

    private static async Task<ChatController.ChatSearchResultDto> Page(
        ChatController controller,
        int limit = 50,
        string? before = null
    )
    {
        IActionResult result = await controller.GetUserMessages(
            Broadcaster.ToString(),
            "u1",
            limit,
            before
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ((StatusResponseDto<ChatController.ChatSearchResultDto>)ok.Value!).Data!;
    }

    [Fact]
    public async Task Only_this_persons_live_lines_in_this_channel_come_back_newest_first()
    {
        ChatControllerTestDbContext db = await Seed(
            Row("a", Day1),
            Row("b", Day1.AddMinutes(1)),
            Row("other-person", Day1.AddMinutes(2), userId: "u2"),
            Row("deleted", Day1.AddMinutes(3), deletedAt: Day1.AddMinutes(4)),
            Row("foreign", Day1.AddMinutes(5), broadcaster: OtherBroadcaster)
        );

        ChatController.ChatSearchResultDto page = await Page(Build(db));

        page.Messages.Select(m => m.Id).Should().Equal("b", "a");
        page.Messages[0].Message.Should().Be("line b");
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task The_cursor_walks_back_to_the_first_line_and_then_ends()
    {
        // Seven lines; two share one timestamp so the Id breaks the tie.
        ChatControllerTestDbContext db = await Seed(
            Row("m1", Day1),
            Row("m2", Day1.AddDays(1)),
            Row("m3a", Day1.AddDays(2)),
            Row("m3b", Day1.AddDays(2)),
            Row("m4", Day1.AddDays(3)),
            Row("m5", Day1.AddDays(4)),
            Row("m6", Day1.AddDays(5))
        );
        ChatController controller = Build(db);

        List<string> seen = [];
        string? cursor = null;
        int pages = 0;
        do
        {
            ChatController.ChatSearchResultDto page = await Page(controller, 3, cursor);
            seen.AddRange(page.Messages.Select(m => m.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        seen.Should().Equal("m6", "m5", "m4", "m3b", "m3a", "m2", "m1");
        pages.Should().Be(3);
    }

    [Fact]
    public async Task A_cursor_the_server_did_not_issue_is_a_bad_request()
    {
        ChatControllerTestDbContext db = await Seed(Row("a", Day1));

        IActionResult result = await Build(db)
            .GetUserMessages(Broadcaster.ToString(), "u1", 50, "not-a-cursor!");

        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ((StatusResponseDto<object>)bad.Value!).Code.Should().Be("invalid_cursor");
    }
}
