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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Proves the audit interceptor stamps a new row with the clock only when the writer left CreatedAt unset, so a
/// row that records a past fact (a replayed chat message) keeps the time the fact happened.
/// </summary>
public sealed class AuditableEntityInterceptorTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static AuthDbContext Build() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite(AuthTestBuilder.SharedDatabase(Guid.NewGuid().ToString()))
                .AddInterceptors(new AuditableEntityInterceptor(new FixedTime(Now)))
                .Options
        );

    private static ChatMessage Row(string id, DateTime createdAt) =>
        new()
        {
            Id = id,
            BroadcasterId = Guid.NewGuid(),
            UserId = "u",
            Username = "u",
            DisplayName = "U",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = createdAt,
        };

    [Fact]
    public async Task Keeps_a_created_at_the_writer_already_set()
    {
        await using AuthDbContext db = Build();
        DateTime past = new(2025, 8, 14, 19, 30, 5, DateTimeKind.Utc);
        db.ChatMessages.Add(Row("past", past));

        await db.SaveChangesAsync();

        ChatMessage saved = await db.ChatMessages.AsNoTracking().SingleAsync();
        saved.CreatedAt.Should().Be(past);
        saved.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Stamps_the_clock_when_the_writer_left_created_at_unset()
    {
        await using AuthDbContext db = Build();
        db.ChatMessages.Add(Row("fresh", default));

        await db.SaveChangesAsync();

        ChatMessage saved = await db.ChatMessages.AsNoTracking().SingleAsync();
        saved.CreatedAt.Should().Be(Now);
        saved.UpdatedAt.Should().Be(Now);
    }

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }
}
