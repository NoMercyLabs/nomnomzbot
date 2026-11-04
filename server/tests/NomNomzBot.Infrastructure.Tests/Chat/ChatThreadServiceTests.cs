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
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat;

public sealed class ChatThreadServiceTests
{
    [Fact]
    public async Task ReadThread_returns_root_and_every_descendant_in_order_with_channel_isolation()
    {
        Guid channel = Guid.CreateVersion7();
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChatMessages.AddRange(
            Line("root", channel, 1),
            Line("middle", channel, 2, "root", "root"),
            Line("other-branch", channel, 3, "root", "root"),
            Line("leaf", channel, 4, "middle", "root"),
            Line("foreign", Guid.CreateVersion7(), 5, "root", "root")
        );
        await db.SaveChangesAsync();

        ChatThreadService service = new(db);
        IReadOnlyList<ChatMessage> rows = (await service.ReadAsync(channel, "leaf")).Value;

        rows.Select(row => row.Id).Should().Equal("root", "middle", "other-branch", "leaf");
    }

    [Fact]
    public async Task ReadThread_follows_legacy_parent_links_when_thread_ids_were_not_stored()
    {
        Guid channel = Guid.CreateVersion7();
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChatMessages.AddRange(
            Line("root", channel, 1),
            Line("middle", channel, 2, "root"),
            Line("leaf", channel, 3, "middle"),
            Line("branch", channel, 4, "root")
        );
        await db.SaveChangesAsync();

        ChatThreadService service = new(db);
        IReadOnlyList<ChatMessage> rows = (await service.ReadAsync(channel, "leaf")).Value;

        rows.Select(row => row.Id).Should().Equal("root", "middle", "leaf", "branch");
    }

    private static ChatMessage Line(
        string id,
        Guid channel,
        int minute,
        string? parent = null,
        string? root = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = channel,
            UserId = "viewer",
            Username = "viewer",
            DisplayName = "Viewer",
            UserType = "viewer",
            Message = id,
            ReplyToMessageId = parent,
            ReplyThreadParentMessageId = root,
            CreatedAt = new DateTime(2026, 11, 4, 12, minute, 0, DateTimeKind.Utc),
        };
}
