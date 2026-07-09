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
using NomNomzBot.Domain.Identity.Entities;
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
        new(
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>()
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
}
