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
/// Proves <c>GET chat/search</c> over real persisted rows: a typo query finds the message it meant, unrelated and
/// deleted and foreign-channel rows never come back, results are newest first, and paging by the cursor walks back
/// to the channel's very first message and then ends.
/// </summary>
public sealed class ChatSearchTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid OtherBroadcaster = Guid.CreateVersion7();
    private static readonly DateTime Day1 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ChatMessage Row(
        string id,
        string message,
        DateTime createdAt,
        Guid? broadcaster = null,
        DateTime? deletedAt = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster ?? Broadcaster,
            UserId = "u1",
            Username = "viewer1",
            DisplayName = "Viewer1",
            UserType = "viewer",
            Message = message,
            Fragments = [new() { Type = "text", Text = message }],
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

    private static async Task<ChatController.ChatSearchResultDto> Search(
        ChatController controller,
        string query,
        int limit = 50,
        string? before = null
    )
    {
        IActionResult result = await controller.SearchMessages(
            Broadcaster.ToString(),
            query,
            limit,
            before
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ((StatusResponseDto<ChatController.ChatSearchResultDto>)ok.Value!).Data!;
    }

    [Fact]
    public async Task A_typo_query_finds_the_message_it_meant_and_skips_unrelated_ones()
    {
        ChatControllerTestDbContext db = await Seed(
            Row("hit", "hello world", Day1),
            Row("miss", "the stream is great today", Day1.AddMinutes(1))
        );

        ChatController.ChatSearchResultDto page = await Search(Build(db), "hello wrld");

        page.Messages.Select(m => m.Id).Should().Equal("hit");
        page.Messages[0].Message.Should().Be("hello world");
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task The_match_ignores_letter_case()
    {
        ChatControllerTestDbContext db = await Seed(Row("hit", "HELLO WORLD", Day1));

        ChatController.ChatSearchResultDto page = await Search(Build(db), "Hello World");

        page.Messages.Select(m => m.Id).Should().Equal("hit");
    }

    [Fact]
    public async Task A_one_character_query_is_a_bad_request_and_reads_nothing()
    {
        ChatControllerTestDbContext db = await Seed(Row("a", "a", Day1));

        IActionResult result = await Build(db).SearchMessages(Broadcaster.ToString(), "a");

        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        StatusResponseDto<object> body = (StatusResponseDto<object>)bad.Value!;
        body.Status.Should().Be("error");
        body.Code.Should().Be("invalid_query");
    }

    [Fact]
    public async Task A_cursor_the_service_did_not_issue_is_a_bad_request()
    {
        ChatControllerTestDbContext db = await Seed(Row("a", "hello world", Day1));

        IActionResult result = await Build(db)
            .SearchMessages(Broadcaster.ToString(), "hello", 50, "not-a-cursor!");

        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ((StatusResponseDto<object>)bad.Value!).Code.Should().Be("invalid_cursor");
    }

    [Fact]
    public async Task Deleted_messages_and_other_channels_messages_are_never_returned()
    {
        ChatControllerTestDbContext db = await Seed(
            Row("live", "hello world", Day1),
            Row("deleted", "hello world", Day1.AddMinutes(1), deletedAt: Day1.AddMinutes(2)),
            Row("foreign", "hello world", Day1.AddMinutes(3), OtherBroadcaster)
        );

        ChatController.ChatSearchResultDto page = await Search(Build(db), "hello world");

        page.Messages.Select(m => m.Id).Should().Equal("live");
    }

    [Fact]
    public async Task Paging_by_cursor_walks_back_to_the_first_message_newest_first_then_ends()
    {
        // Seven matches over several days, including two that share one timestamp (the Id breaks the tie),
        // with non-matching chatter between them.
        ChatControllerTestDbContext db = await Seed(
            Row("m1", "hello world one", Day1),
            Row("x1", "completely different", Day1.AddHours(1)),
            Row("m2", "hello world two", Day1.AddDays(1)),
            Row("m3a", "hello world three", Day1.AddDays(2)),
            Row("m3b", "hello world three again", Day1.AddDays(2)),
            Row("x2", "nothing to see here", Day1.AddDays(3)),
            Row("m4", "hello world four", Day1.AddDays(4)),
            Row("m5", "hello world five", Day1.AddDays(5))
        );
        ChatController controller = Build(db);

        List<string> seen = [];
        string? cursor = null;
        int pages = 0;
        do
        {
            ChatController.ChatSearchResultDto page = await Search(
                controller,
                "hello world",
                limit: 2,
                before: cursor
            );
            seen.AddRange(page.Messages.Select(m => m.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        seen.Should().Equal("m5", "m4", "m3b", "m3a", "m2", "m1");
        cursor.Should().BeNull();
    }

    [Fact]
    public async Task A_page_stops_at_the_limit_and_hands_back_a_cursor_that_resumes_after_it()
    {
        ChatControllerTestDbContext db = await Seed(
            Row("m1", "hello world one", Day1),
            Row("m2", "hello world two", Day1.AddDays(1)),
            Row("m3", "hello world three", Day1.AddDays(2))
        );
        ChatController controller = Build(db);

        ChatController.ChatSearchResultDto first = await Search(controller, "hello world", 2);
        first.Messages.Select(m => m.Id).Should().Equal("m3", "m2");
        first.NextCursor.Should().NotBeNullOrEmpty();

        ChatController.ChatSearchResultDto second = await Search(
            controller,
            "hello world",
            2,
            first.NextCursor
        );
        second.Messages.Select(m => m.Id).Should().Equal("m1");
        second.NextCursor.Should().BeNull();
    }
}
