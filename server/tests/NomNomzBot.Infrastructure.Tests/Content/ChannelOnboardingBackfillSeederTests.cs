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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Content;

public sealed class ChannelOnboardingBackfillSeederTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Backfill_adds_missing_connections_promotes_only_owner_signed_in_tenants_and_is_idempotent()
    {
        using AuthDbContext db = AuthTestBuilder.NewContext();

        Channel onboarded = await SeedChannelAsync(db, "tw-1", "onboarded", isOnboarded: true);
        Channel ownerSignedIn = await SeedChannelAsync(db, "tw-2", "signedin", isOnboarded: false);
        Channel moderatorOnly = await SeedChannelAsync(db, "tw-3", "modonly", isOnboarded: false);

        // The owner's own Twitch grant, vaulted on their tenant — written only by their sign-in.
        SeedTwitchGrant(db, ownerSignedIn.Id, "tw-2");
        // A grant on the moderator-only tenant from a different Twitch account is no owner evidence.
        SeedTwitchGrant(db, moderatorOnly.Id, "tw-999");
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);
        await AssertBackfilledAsync(db, onboarded, ownerSignedIn, moderatorOnly);

        await RunBackfillAsync(db);
        await AssertBackfilledAsync(db, onboarded, ownerSignedIn, moderatorOnly);
    }

    [Fact]
    public async Task A_revoked_owner_grant_does_not_promote()
    {
        using AuthDbContext db = AuthTestBuilder.NewContext();
        Channel channel = await SeedChannelAsync(db, "tw-4", "revoked", isOnboarded: false);
        SeedTwitchGrant(db, channel.Id, "tw-4", deletedAt: FixedNow.UtcDateTime.AddDays(-1));
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        Channel after = await db.Channels.AsNoTracking().SingleAsync();
        after.IsOnboarded.Should().BeFalse();
        (await db.PlatformConnections.CountAsync()).Should().Be(0);
    }

    private static async Task AssertBackfilledAsync(
        AuthDbContext db,
        Channel onboarded,
        Channel ownerSignedIn,
        Channel moderatorOnly
    )
    {
        List<Channel> channels = await db.Channels.AsNoTracking().ToListAsync();
        List<PlatformConnection> connections = await db
            .PlatformConnections.AsNoTracking()
            .ToListAsync();

        connections.Should().HaveCount(2);

        Channel first = channels.Single(c => c.Id == onboarded.Id);
        first.BotJoinedAt.Should().BeNull("a connection backfill does not invent a join time");
        PlatformConnection firstConnection = connections.Single(p => p.ChannelId == onboarded.Id);
        firstConnection.Provider.Should().Be(AuthEnums.Platform.Twitch);
        firstConnection.ExternalChannelId.Should().Be("tw-1");
        firstConnection.DisplayName.Should().Be("Onboarded");
        firstConnection.IsPrimary.Should().BeTrue();

        Channel promoted = channels.Single(c => c.Id == ownerSignedIn.Id);
        promoted.IsOnboarded.Should().BeTrue();
        promoted.BotJoinedAt.Should().Be(FixedNow.UtcDateTime);
        PlatformConnection promotedConnection = connections.Single(p =>
            p.ChannelId == ownerSignedIn.Id
        );
        promotedConnection.ExternalChannelId.Should().Be("tw-2");
        promotedConnection.DisplayName.Should().Be("Signedin");
        promotedConnection.IsPrimary.Should().BeTrue();

        Channel untouched = channels.Single(c => c.Id == moderatorOnly.Id);
        untouched.IsOnboarded.Should().BeFalse();
        untouched.BotJoinedAt.Should().BeNull();
        connections.Should().NotContain(p => p.ChannelId == moderatorOnly.Id);
    }

    // Mirrors SeedRunner: the seeder leaves its writes tracked and the runner commits them.
    private static async Task RunBackfillAsync(AuthDbContext db)
    {
        await new ChannelOnboardingBackfillSeeder(
            db,
            new FakeTimeProvider(FixedNow),
            NullLogger<ChannelOnboardingBackfillSeeder>.Instance
        ).SeedAsync();
        await db.SaveChangesAsync();
    }

    private static async Task<Channel> SeedChannelAsync(
        AuthDbContext db,
        string twitchId,
        string login,
        bool isOnboarded
    )
    {
        User owner = new()
        {
            TwitchUserId = twitchId,
            Username = login,
            UsernameNormalized = login,
            DisplayName = char.ToUpperInvariant(login[0]) + login[1..],
        };
        Channel channel = new()
        {
            OwnerUserId = owner.Id,
            TwitchChannelId = twitchId,
            ExternalChannelId = twitchId,
            Name = login,
            NameNormalized = login,
            IsOnboarded = isOnboarded,
        };
        db.Users.Add(owner);
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return channel;
    }

    private static void SeedTwitchGrant(
        AuthDbContext db,
        Guid channelId,
        string accountId,
        DateTime? deletedAt = null
    ) =>
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = channelId,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                ProviderAccountId = accountId,
                Status = "connected",
                DeletedAt = deletedAt,
            }
        );
}
