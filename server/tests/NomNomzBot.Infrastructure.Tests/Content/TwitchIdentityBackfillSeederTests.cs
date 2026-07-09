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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// Behavioural proof for <see cref="TwitchIdentityBackfillSeeder"/> (platform-identity §8.1): every existing
/// user gains a primary <c>twitch</c> identity enriched from its own profile columns, and a re-run adds
/// nothing (the idempotency contract every seeder must honour).
/// </summary>
public sealed class TwitchIdentityBackfillSeederTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private static AuthDbContext NewContext(string dbName) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );

    [Fact]
    public async Task Seeds_a_primary_twitch_identity_enriched_from_the_user_row()
    {
        using AuthDbContext ctx = NewContext(Guid.NewGuid().ToString());
        User user = new()
        {
            TwitchUserId = "555",
            Username = "streamer",
            UsernameNormalized = "streamer",
            DisplayName = "Streamer",
            ProfileImageUrl = "https://cdn/avatar.png",
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        await new TwitchIdentityBackfillSeeder(ctx, new FakeTimeProvider(FixedNow)).SeedAsync();
        await ctx.SaveChangesAsync();

        UserIdentity identity = await ctx.UserIdentities.SingleAsync();
        identity.UserId.Should().Be(user.Id);
        identity.Provider.Should().Be("twitch");
        identity.ProviderUserId.Should().Be("555");
        identity.ProviderUsername.Should().Be("streamer");
        identity.ProviderDisplayName.Should().Be("Streamer");
        identity.ProviderAvatarUrl.Should().Be("https://cdn/avatar.png");
        identity.IsPrimary.Should().BeTrue();
        identity.LinkedAt.Should().Be(FixedNow.UtcDateTime);
    }

    [Fact]
    public async Task A_second_run_is_idempotent_and_adds_no_duplicate()
    {
        using AuthDbContext ctx = NewContext(Guid.NewGuid().ToString());
        ctx.Users.Add(
            new User
            {
                TwitchUserId = "1",
                Username = "a",
                UsernameNormalized = "a",
                DisplayName = "A",
            }
        );
        await ctx.SaveChangesAsync();

        TwitchIdentityBackfillSeeder seeder = new(ctx, new FakeTimeProvider(FixedNow));
        await seeder.SeedAsync();
        await ctx.SaveChangesAsync();
        await seeder.SeedAsync();
        await ctx.SaveChangesAsync();

        (await ctx.UserIdentities.CountAsync()).Should().Be(1);
    }
}
