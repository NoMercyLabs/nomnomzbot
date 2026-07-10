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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="UserIdentityService"/> (platform-identity §3.1) — the resolver every
/// ingest path routes through. Reuses <see cref="AuthDbContext"/> (which maps both <c>Users</c> and
/// <c>UserIdentities</c>) over EF InMemory, wired through a real container so the service's dedicated-scope
/// write path shares the same store.
/// </summary>
public sealed class UserIdentityServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AuthDbContext>(o =>
            o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        );
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedNow));
        return services.BuildServiceProvider();
    }

    private static UserIdentityService NewService(IServiceScope scope) =>
        NewService(scope, new RecordingEventBus());

    private static UserIdentityService NewService(IServiceScope scope, RecordingEventBus events) =>
        new(
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            events
        );

    [Fact]
    public async Task ResolveUserAsync_absent_without_getOrCreate_fails_with_IDENTITY_NOT_FOUND()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Result<Guid> result = await NewService(scope)
            .ResolveUserAsync("twitch", "999", getOrCreate: false);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("IDENTITY_NOT_FOUND");
    }

    [Fact]
    public async Task ResolveUserAsync_getOrCreate_creates_user_and_primary_twitch_identity()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Result<Guid> result = await NewService(scope)
            .ResolveUserAsync("twitch", "12345", getOrCreate: true);

        result.IsSuccess.Should().BeTrue();
        Guid userId = result.Value;

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        UserIdentity identity = await db.UserIdentities.SingleAsync(i =>
            i.ProviderUserId == "12345"
        );
        identity.Provider.Should().Be("twitch");
        identity.UserId.Should().Be(userId);
        identity.IsPrimary.Should().BeTrue();
        identity.LinkedAt.Should().Be(FixedNow.UtcDateTime);

        User user = await db.Users.SingleAsync(u => u.Id == userId);
        user.TwitchUserId.Should().Be("12345");
        user.Platform.Should().Be("twitch");
    }

    [Fact]
    public async Task ResolveUserAsync_is_idempotent_no_duplicate_identity()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        UserIdentityService svc = NewService(scope);

        Guid first = (await svc.ResolveUserAsync("twitch", "42", getOrCreate: true)).Value;
        Guid second = (await svc.ResolveUserAsync("twitch", "42", getOrCreate: true)).Value;

        second.Should().Be(first);

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        int count = await db.UserIdentities.CountAsync(i =>
            i.Provider == "twitch" && i.ProviderUserId == "42"
        );
        count.Should().Be(1);
    }

    [Fact]
    public async Task ResolveUserAsync_reuses_a_pre_identity_twitch_user()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope arrangeScope = provider.CreateScope();

        // A user created before the identity table (only TwitchUserId), with no UserIdentity row.
        IApplicationDbContext arrangeDb =
            arrangeScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        User legacy = new()
        {
            TwitchUserId = "777",
            Username = "legacy",
            UsernameNormalized = "legacy",
            DisplayName = "Legacy",
        };
        arrangeDb.Users.Add(legacy);
        await arrangeDb.SaveChangesAsync();

        using IServiceScope actScope = provider.CreateScope();
        Result<Guid> result = await NewService(actScope)
            .ResolveUserAsync("twitch", "777", getOrCreate: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(legacy.Id);

        IApplicationDbContext db =
            actScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        UserIdentity identity = await db.UserIdentities.SingleAsync(i => i.ProviderUserId == "777");
        identity.UserId.Should().Be(legacy.Id);
        identity.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_returns_all_identities_primary_first()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        UserIdentityService svc = NewService(scope);

        Guid userId = (await svc.ResolveUserAsync("twitch", "100", getOrCreate: true)).Value;

        // A second, non-primary linked identity (added directly — provider linking arrives in a later slice).
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = userId,
                Provider = "youtube",
                ProviderUserId = "yt-100",
                ProviderUsername = "yt-user",
                IsPrimary = false,
                LinkedAt = FixedNow.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        Result<IReadOnlyList<UserIdentityDto>> list = await svc.ListAsync(userId);

        list.IsSuccess.Should().BeTrue();
        list.Value.Should().HaveCount(2);
        list.Value[0].IsPrimary.Should().BeTrue();
        list.Value[0].Provider.Should().Be("twitch");
        list.Value.Select(i => i.Provider).Should().BeEquivalentTo(["twitch", "youtube"]);
    }

    [Fact]
    public async Task ResolveUserAsync_creates_a_non_twitch_user_with_null_twitch_id()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Result<Guid> result = await NewService(scope)
            .ResolveUserAsync("youtube", "yt-1", getOrCreate: true);

        result.IsSuccess.Should().BeTrue();

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        User user = await db.Users.SingleAsync(u => u.Id == result.Value);
        user.TwitchUserId.Should().BeNull();
        user.Platform.Should().Be("youtube");

        UserIdentity identity = await db.UserIdentities.SingleAsync(i =>
            i.ProviderUserId == "yt-1"
        );
        identity.Provider.Should().Be("youtube");
        identity.IsPrimary.Should().BeTrue();
    }

    // ── Link ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_attaches_a_non_primary_identity_and_publishes_the_linked_event()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        RecordingEventBus events = new();

        Result<UserIdentityDto> result = await NewService(scope, events)
            .LinkAsync(
                userId,
                new ExternalIdentityProof(
                    "Kick",
                    "k-1",
                    "kicker",
                    "Kicker",
                    "https://cdn/k.png",
                    null
                )
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("kick");
        result.Value.IsPrimary.Should().BeFalse();

        UserIdentity linked = await db.UserIdentities.SingleAsync(i => i.Provider == "kick");
        linked.UserId.Should().Be(userId);
        linked.IsPrimary.Should().BeFalse();
        linked.ProviderUsername.Should().Be("kicker");

        // The original primary is untouched — the account still has exactly one primary.
        (await db.UserIdentities.CountAsync(i => i.UserId == userId && i.IsPrimary))
            .Should()
            .Be(1);

        events
            .Published.OfType<UserIdentityLinkedEvent>()
            .Should()
            .ContainSingle(e =>
                e.UserId == userId && e.Provider == "kick" && e.ProviderUserId == "k-1"
            );
    }

    [Fact]
    public async Task LinkAsync_is_idempotent_for_the_callers_own_identity()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        UserIdentityService svc = NewService(scope);

        await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
        );
        Result<UserIdentityDto> second = await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker-renamed", null, null, null)
        );

        second.IsSuccess.Should().BeTrue();
        (await db.UserIdentities.CountAsync(i => i.Provider == "kick")).Should().Be(1);
        (await db.UserIdentities.SingleAsync(i => i.Provider == "kick"))
            .ProviderUsername.Should()
            .Be("kicker-renamed");
    }

    [Fact]
    public async Task LinkAsync_refuses_an_account_already_linked_to_another_user()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid ownerId = await SeedUserWithPrimaryAsync(db, "kick", "k-1");
        Guid otherId = await SeedUserWithPrimaryAsync(db, "twitch", "200");

        Result<UserIdentityDto> result = await NewService(scope)
            .LinkAsync(
                otherId,
                new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("IDENTITY_ALREADY_LINKED");
        // The account stays with its original owner.
        (await db.UserIdentities.SingleAsync(i => i.Provider == "kick"))
            .UserId.Should()
            .Be(ownerId);
    }

    [Fact]
    public async Task LinkAsync_refuses_a_second_identity_for_an_already_linked_provider()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        UserIdentityService svc = NewService(scope);
        await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
        );

        Result<UserIdentityDto> result = await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-2", "other-kick", null, null, null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_ALREADY_LINKED");
        (await db.UserIdentities.CountAsync(i => i.UserId == userId && i.Provider == "kick"))
            .Should()
            .Be(1);
    }

    // ── Unlink ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlinkAsync_refuses_the_primary_identity()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        UserIdentityService svc = NewService(scope);
        await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
        );
        Guid primaryId = (await db.UserIdentities.SingleAsync(i => i.IsPrimary)).Id;

        Result result = await svc.UnlinkAsync(userId, primaryId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PRIMARY_IDENTITY");
        (await db.UserIdentities.AnyAsync(i => i.Id == primaryId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkAsync_refuses_the_last_identity()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        // A user with a single, non-primary identity — the primary guard can't fire, so the last-identity guard must.
        User user = new()
        {
            Platform = "kick",
            Username = "solo",
            UsernameNormalized = "solo",
            DisplayName = "Solo",
            Enabled = true,
        };
        db.Users.Add(user);
        UserIdentity only = new()
        {
            UserId = user.Id,
            Provider = "kick",
            ProviderUserId = "k-1",
            ProviderUsername = "solo",
            IsPrimary = false,
            LinkedAt = FixedNow.UtcDateTime,
        };
        db.UserIdentities.Add(only);
        await db.SaveChangesAsync();

        Result result = await NewService(scope).UnlinkAsync(user.Id, only.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("LAST_IDENTITY");
        (await db.UserIdentities.AnyAsync(i => i.Id == only.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkAsync_removes_a_non_primary_identity_and_publishes_the_unlinked_event()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        RecordingEventBus events = new();
        UserIdentityService svc = NewService(scope, events);
        await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
        );
        Guid kickId = (await db.UserIdentities.SingleAsync(i => i.Provider == "kick")).Id;

        Result result = await svc.UnlinkAsync(userId, kickId);

        result.IsSuccess.Should().BeTrue();
        (await db.UserIdentities.AnyAsync(i => i.Id == kickId)).Should().BeFalse();
        // The primary survives.
        (await db.UserIdentities.CountAsync(i => i.UserId == userId && i.IsPrimary))
            .Should()
            .Be(1);
        events
            .Published.OfType<UserIdentityUnlinkedEvent>()
            .Should()
            .ContainSingle(e =>
                e.UserId == userId && e.Provider == "kick" && e.Reason == "user_unlinked"
            );
    }

    [Fact]
    public async Task UnlinkAsync_not_found_for_a_foreign_identity_id()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");

        Result result = await NewService(scope).UnlinkAsync(userId, Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("IDENTITY_NOT_FOUND");
    }

    // ── Set primary ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetPrimaryAsync_moves_the_marker_updates_platform_and_publishes_the_event()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        RecordingEventBus events = new();
        UserIdentityService svc = NewService(scope, events);
        await svc.LinkAsync(
            userId,
            new ExternalIdentityProof("kick", "k-1", "kicker", null, null, null)
        );
        Guid kickId = (await db.UserIdentities.SingleAsync(i => i.Provider == "kick")).Id;

        Result<UserIdentityDto> result = await svc.SetPrimaryAsync(userId, kickId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("kick");
        result.Value.IsPrimary.Should().BeTrue();

        (await db.UserIdentities.SingleAsync(i => i.Provider == "kick"))
            .IsPrimary.Should()
            .BeTrue();
        (await db.UserIdentities.SingleAsync(i => i.Provider == "twitch"))
            .IsPrimary.Should()
            .BeFalse();
        (await db.UserIdentities.CountAsync(i => i.UserId == userId && i.IsPrimary)).Should().Be(1);
        (await db.Users.SingleAsync(u => u.Id == userId)).Platform.Should().Be("kick");

        events
            .Published.OfType<PrimaryIdentityChangedEvent>()
            .Should()
            .ContainSingle(e => e.UserId == userId && e.Provider == "kick");
    }

    [Fact]
    public async Task SetPrimaryAsync_not_found_for_a_foreign_identity_id()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid userId = await SeedUserWithPrimaryAsync(db, "twitch", "100");

        Result<UserIdentityDto> result = await NewService(scope)
            .SetPrimaryAsync(userId, Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("IDENTITY_NOT_FOUND");
    }

    // ── Merge (viewer absorption) ─────────────────────────────────────────────

    [Fact]
    public async Task MergeIdentitiesAsync_reparents_absorbed_identities_and_keeps_one_primary()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid survivor = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        Guid absorbed = await SeedUserWithPrimaryAsync(db, "kick", "k-1");
        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = absorbed,
                Provider = "youtube",
                ProviderUserId = "yt-1",
                ProviderUsername = "yter",
                IsPrimary = false,
                LinkedAt = FixedNow.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        Result result = await NewService(scope).MergeIdentitiesAsync(survivor, absorbed);

        result.IsSuccess.Should().BeTrue();
        // Both absorbed identities now belong to the survivor, as non-primary.
        (await db.UserIdentities.CountAsync(i => i.UserId == absorbed))
            .Should()
            .Be(0);
        List<UserIdentity> survivorIdentities = await db
            .UserIdentities.Where(i => i.UserId == survivor)
            .ToListAsync();
        survivorIdentities
            .Select(i => i.Provider)
            .Should()
            .BeEquivalentTo(["twitch", "kick", "youtube"]);
        survivorIdentities.Count(i => i.IsPrimary).Should().Be(1);
        survivorIdentities.Single(i => i.IsPrimary).Provider.Should().Be("twitch");
    }

    [Fact]
    public async Task MergeIdentitiesAsync_drops_a_provider_the_survivor_already_holds()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Guid survivor = await SeedUserWithPrimaryAsync(db, "twitch", "100");
        Guid absorbed = await SeedUserWithPrimaryAsync(db, "twitch", "999");

        Result result = await NewService(scope).MergeIdentitiesAsync(survivor, absorbed);

        result.IsSuccess.Should().BeTrue();
        // The duplicate twitch identity is dropped, never re-parented — the survivor keeps exactly its own.
        List<UserIdentity> survivorIdentities = await db
            .UserIdentities.Where(i => i.UserId == survivor)
            .ToListAsync();
        survivorIdentities.Should().ContainSingle();
        survivorIdentities[0].ProviderUserId.Should().Be("100");
        (await db.UserIdentities.AnyAsync(i => i.ProviderUserId == "999")).Should().BeFalse();
    }

    [Fact]
    public async Task MergeIdentitiesAsync_promotes_a_primary_when_the_survivor_had_none()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        // A survivor with no identities at all (e.g. a bare row) merges an absorbed user whose primary is kick.
        User survivor = new()
        {
            Platform = "twitch",
            Username = "bare",
            UsernameNormalized = "bare",
            DisplayName = "Bare",
            Enabled = true,
        };
        db.Users.Add(survivor);
        await db.SaveChangesAsync();
        Guid absorbed = await SeedUserWithPrimaryAsync(db, "kick", "k-1");

        Result result = await NewService(scope).MergeIdentitiesAsync(survivor.Id, absorbed);

        result.IsSuccess.Should().BeTrue();
        List<UserIdentity> survivorIdentities = await db
            .UserIdentities.Where(i => i.UserId == survivor.Id)
            .ToListAsync();
        survivorIdentities.Should().ContainSingle();
        survivorIdentities[0].Provider.Should().Be("kick");
        // Never leave an orphaned primary — the moved identity is promoted.
        survivorIdentities[0].IsPrimary.Should().BeTrue();
    }

    private static async Task<Guid> SeedUserWithPrimaryAsync(
        IApplicationDbContext db,
        string provider,
        string providerUserId
    )
    {
        User user = new()
        {
            TwitchUserId = provider == "twitch" ? providerUserId : null,
            Platform = provider,
            Username = providerUserId,
            UsernameNormalized = providerUserId,
            DisplayName = providerUserId,
            Enabled = true,
        };
        db.Users.Add(user);
        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = user.Id,
                Provider = provider,
                ProviderUserId = providerUserId,
                ProviderUsername = providerUserId,
                IsPrimary = true,
                LinkedAt = FixedNow.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
        return user.Id;
    }
}
