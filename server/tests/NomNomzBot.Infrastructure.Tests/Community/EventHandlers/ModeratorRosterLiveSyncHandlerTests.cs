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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Community.EventHandlers;

/// <summary>
/// Proves the live moderator events keep the <c>ChannelModerators</c> roster (what the People page reads) in
/// step with Twitch: an add stores the person for that channel only, a remove deletes only that channel's row,
/// and a re-delivered or re-added event never duplicates or collides.
/// </summary>
public sealed class ModeratorRosterLiveSyncHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d001");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000d002");
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static (ModeratorRosterLiveSyncHandler Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        foreach ((Guid id, string name) in new[] { (Channel, "one"), (OtherChannel, "two") })
            db.Channels.Add(
                new()
                {
                    Id = id,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = "tw-" + name,
                    Name = name,
                    NameNormalized = name,
                }
            );
        db.SaveChanges();

        ModeratorRosterLiveSyncHandler sut = new(
            db,
            new FakeTimeProvider(Now),
            NullLogger<ModeratorRosterLiveSyncHandler>.Instance
        );
        return (sut, db);
    }

    private static ModeratorAddedEvent Added(Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            UserId = "tw-mod-1",
            UserDisplayName = "ModOne",
            UserLogin = "modone",
        };

    private static ModeratorRemovedEvent Removed(Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            UserId = "tw-mod-1",
            UserDisplayName = "ModOne",
            UserLogin = "modone",
        };

    [Fact]
    public async Task Add_creates_the_user_and_a_moderator_row_for_that_channel_only()
    {
        (ModeratorRosterLiveSyncHandler sut, AuthDbContext db) = Build();

        await sut.HandleAsync(Added(Channel));

        User user = await db.Users.SingleAsync(u => u.TwitchUserId == "tw-mod-1");
        user.Username.Should().Be("modone");
        user.DisplayName.Should().Be("ModOne");

        ChannelModerator row = await db.ChannelModerators.SingleAsync();
        row.ChannelId.Should().Be(Channel);
        row.UserId.Should().Be(user.Id);
        row.GrantedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Add_twice_keeps_one_row_and_one_user()
    {
        (ModeratorRosterLiveSyncHandler sut, AuthDbContext db) = Build();

        await sut.HandleAsync(Added(Channel));
        await sut.HandleAsync(Added(Channel));

        (await db.ChannelModerators.CountAsync()).Should().Be(1);
        (await db.Users.CountAsync(u => u.TwitchUserId == "tw-mod-1")).Should().Be(1);
    }

    [Fact]
    public async Task Remove_deletes_the_row_of_that_channel_and_keeps_the_other_channel()
    {
        (ModeratorRosterLiveSyncHandler sut, AuthDbContext db) = Build();
        await sut.HandleAsync(Added(Channel));
        await sut.HandleAsync(Added(OtherChannel));

        await sut.HandleAsync(Removed(Channel));

        List<ChannelModerator> rows = await db.ChannelModerators.ToListAsync();
        rows.Should().ContainSingle().Which.ChannelId.Should().Be(OtherChannel);
    }

    [Fact]
    public async Task Remove_for_a_person_who_was_never_stored_changes_nothing()
    {
        (ModeratorRosterLiveSyncHandler sut, AuthDbContext db) = Build();

        await sut.HandleAsync(Removed(Channel));

        (await db.ChannelModerators.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Add_after_remove_restores_the_moderator()
    {
        (ModeratorRosterLiveSyncHandler sut, AuthDbContext db) = Build();
        await sut.HandleAsync(Added(Channel));
        await sut.HandleAsync(Removed(Channel));

        await sut.HandleAsync(Added(Channel));

        ChannelModerator row = await db.ChannelModerators.SingleAsync();
        row.ChannelId.Should().Be(Channel);
        row.DeletedAt.Should().BeNull();
    }
}
