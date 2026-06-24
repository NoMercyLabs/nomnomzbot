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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the set-based actor backfill: after the channel-event-log fold leaves <c>UserId</c> null with the actor's
/// Twitch id snapshotted in <c>Data.actorTwitchUserId</c>, the backfill get-or-creates the Users (DB-only, no Twitch
/// call) and links every row's FK in one pass. Asserts the resulting state — every attributable row carries the
/// internal User Guid of its actor, distinct viewers > 0, a brand-new actor mints exactly one User, an anonymous row
/// stays unlinked — and that a re-run is idempotent. Tenant-scoped: a foreign channel's rows are never touched.
/// </summary>
public sealed class ChannelEventActorBackfillTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000abd01");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000abd02");
    private static readonly DateTime At = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Links_every_row_to_its_actor_user_get_or_creating_in_bulk()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();

        // An EXISTING user (the chatter "100") plus a brand-new actor ("300") the backfill must mint.
        User existing = new()
        {
            TwitchUserId = "100",
            Username = "alice",
            UsernameNormalized = "alice",
            DisplayName = "Alice",
            Enabled = true,
        };
        db.Users.Add(existing);

        db.ChannelEvents.AddRange(
            Row("e1", Channel, "channel.chat.message", Actor("100", "alice", "Alice")),
            Row("e2", Channel, "channel.chat.message", Actor("100", "alice", "Alice")),
            Row("e3", Channel, "channel.raid", Actor("300", "carol", "Carol")),
            // An anonymous cheer carries no actor id — it must remain unlinked.
            Row("e4", Channel, "channel.cheer", """{"eventId":"e4","bits":50}"""),
            // A foreign tenant's row the backfill must not touch.
            Row("e5", OtherChannel, "channel.chat.message", Actor("999", "zed", "Zed"))
        );
        await db.SaveChangesAsync();

        ChannelEventActorBackfill backfill = new(db);
        Result<long> result = await backfill.BackfillAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(3, "e1, e2, e3 each have a resolvable actor; e4 is anonymous");

        List<ChannelEvent> rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e => e.ChannelId == Channel)
            .ToListAsync();

        // The brand-new actor minted exactly one User; the existing user was reused (not duplicated).
        List<User> users = await db.Users.AsNoTracking().ToListAsync();
        users.Should().HaveCount(2, "alice already existed; only carol is newly created");
        Guid aliceId = users.Single(u => u.TwitchUserId == "100").Id;
        Guid carolId = users.Single(u => u.TwitchUserId == "300").Id;
        aliceId.Should().Be(existing.Id, "the existing user is reused by Twitch id");

        rows.Single(r => r.Id == "e1").UserId.Should().Be(aliceId);
        rows.Single(r => r.Id == "e2").UserId.Should().Be(aliceId);
        rows.Single(r => r.Id == "e3").UserId.Should().Be(carolId);
        rows.Single(r => r.Id == "e4").UserId.Should().BeNull("anonymous → unlinked");

        int distinctViewers = rows.Where(r => r.UserId != null)
            .Select(r => r.UserId)
            .Distinct()
            .Count();
        distinctViewers.Should().Be(2, "two distinct viewers — alice and carol");

        // The foreign tenant's row is untouched.
        ChannelEvent foreign = await db.ChannelEvents.AsNoTracking().SingleAsync(e => e.Id == "e5");
        foreign.UserId.Should().BeNull("the backfill is tenant-scoped");

        // Idempotent: a second pass creates no new users and re-links the same rows.
        Result<long> rerun = await backfill.BackfillAsync(Channel);
        rerun.IsSuccess.Should().BeTrue(rerun.ErrorMessage);
        (await db.Users.CountAsync()).Should().Be(2, "no new users on re-run");
        rerun.Value.Should().Be(3, "the same three rows resolve to the same users");
    }

    private static string Actor(string twitchId, string login, string display) =>
        $$"""{"eventId":"x","actorTwitchUserId":"{{twitchId}}","actorLogin":"{{login}}","actorDisplay":"{{display}}"}""";

    private static ChannelEvent Row(string id, Guid channel, string type, string data) =>
        new()
        {
            Id = id,
            ChannelId = channel,
            Type = type,
            Data = data,
            CreatedAt = At,
            UpdatedAt = At,
        };
}
