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
using NomNomzBot.Infrastructure.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="PrimaryIdentityWriter"/> (platform-identity §3.1) — the shared upsert the
/// login, chat and resolver paths call to keep a user's primary identity live. Proves it creates on first
/// sight, enriches a placeholder row (what a real login does to a backfill/resolver placeholder), and stays a
/// no-op — no tracked write — when nothing changed (the hot-path quietness contract).
/// </summary>
public sealed class PrimaryIdentityWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private static AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );

    [Fact]
    public async Task EnsureAsync_creates_a_primary_identity_with_the_full_profile()
    {
        using AuthDbContext ctx = NewContext();
        Guid userId = Guid.CreateVersion7();

        await PrimaryIdentityWriter.EnsureAsync(
            ctx,
            new FakeTimeProvider(FixedNow),
            userId,
            "twitch",
            "500",
            "streamer",
            "Streamer",
            "https://cdn/a.png"
        );
        await ctx.SaveChangesAsync();

        UserIdentity identity = await ctx.UserIdentities.SingleAsync();
        identity.UserId.Should().Be(userId);
        identity.Provider.Should().Be("twitch");
        identity.ProviderUserId.Should().Be("500");
        identity.ProviderUsername.Should().Be("streamer");
        identity.ProviderDisplayName.Should().Be("Streamer");
        identity.ProviderAvatarUrl.Should().Be("https://cdn/a.png");
        identity.IsPrimary.Should().BeTrue();
        identity.LinkedAt.Should().Be(FixedNow.UtcDateTime);
    }

    [Fact]
    public async Task EnsureAsync_enriches_an_existing_placeholder_identity_without_duplicating()
    {
        using AuthDbContext ctx = NewContext();
        Guid userId = Guid.CreateVersion7();
        // A placeholder identity as the resolver/backfill would leave it (username = the external id).
        ctx.UserIdentities.Add(
            new UserIdentity
            {
                UserId = userId,
                Provider = "twitch",
                ProviderUserId = "500",
                ProviderUsername = "500",
                IsPrimary = true,
                LinkedAt = FixedNow.UtcDateTime,
            }
        );
        await ctx.SaveChangesAsync();

        await PrimaryIdentityWriter.EnsureAsync(
            ctx,
            new FakeTimeProvider(FixedNow),
            userId,
            "twitch",
            "500",
            "streamer",
            "Streamer",
            "https://cdn/a.png"
        );
        await ctx.SaveChangesAsync();

        UserIdentity identity = await ctx.UserIdentities.SingleAsync();
        identity.ProviderUsername.Should().Be("streamer");
        identity.ProviderDisplayName.Should().Be("Streamer");
        identity.ProviderAvatarUrl.Should().Be("https://cdn/a.png");
        (await ctx.UserIdentities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EnsureAsync_writes_nothing_when_the_profile_is_unchanged()
    {
        using AuthDbContext ctx = NewContext();
        Guid userId = Guid.CreateVersion7();
        ctx.UserIdentities.Add(
            new UserIdentity
            {
                UserId = userId,
                Provider = "twitch",
                ProviderUserId = "500",
                ProviderUsername = "streamer",
                ProviderDisplayName = "Streamer",
                ProviderAvatarUrl = "https://cdn/a.png",
                IsPrimary = true,
                LinkedAt = FixedNow.UtcDateTime,
            }
        );
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await PrimaryIdentityWriter.EnsureAsync(
            ctx,
            new FakeTimeProvider(FixedNow),
            userId,
            "twitch",
            "500",
            "streamer",
            "Streamer",
            "https://cdn/a.png"
        );

        ctx.ChangeTracker.HasChanges().Should().BeFalse();
    }
}
