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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves feature-flag evaluation (platform-conventions §3.4 / rollout-updates §5): the global toggle, the
/// deterministic rollout-% bucket (0% off, 100% on), an unexpired tenant override winning over the global state,
/// an expired override being ignored, and an unknown flag failing closed.
/// </summary>
public sealed class FeatureFlagServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000007001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FeatureFlagService Sut, AuthDbContext Db) Build(bool tierMet = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        ICacheService cache = Substitute.For<ICacheService>();
        cache.GetAsync<bool?>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((bool?)null); // always a cache miss so evaluation runs
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .IsTierAtLeastAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(tierMet));
        return (new FeatureFlagService(db, tenant, cache, tiers, new FakeTimeProvider(Now)), db);
    }

    private static async Task<FeatureFlag> SeedFlagAsync(
        AuthDbContext db,
        bool global,
        int rollout,
        string key = "feat"
    )
    {
        FeatureFlag flag = new()
        {
            Key = key,
            IsEnabledGlobally = global,
            RolloutPercentage = rollout,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync();
        return flag;
    }

    [Fact]
    public async Task Enabled_when_global_on_at_full_rollout()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build();
        await SeedFlagAsync(db, global: true, rollout: 100);

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_when_global_off()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build();
        await SeedFlagAsync(db, global: false, rollout: 100);

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_at_zero_percent_rollout()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build();
        await SeedFlagAsync(db, global: true, rollout: 0);

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unexpired_override_wins_over_global_off()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build();
        FeatureFlag flag = await SeedFlagAsync(db, global: false, rollout: 0);
        db.FeatureFlagOverrides.Add(
            new FeatureFlagOverride
            {
                FeatureFlagId = flag.Id,
                BroadcasterId = Channel,
                IsEnabled = true,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeTrue();
    }

    [Fact]
    public async Task An_expired_override_is_ignored()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build();
        FeatureFlag flag = await SeedFlagAsync(db, global: false, rollout: 0);
        db.FeatureFlagOverrides.Add(
            new FeatureFlagOverride
            {
                FeatureFlagId = flag.Id,
                BroadcasterId = Channel,
                IsEnabled = true,
                ExpiresAt = Now.UtcDateTime.AddHours(-1),
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_flag_fails_closed()
    {
        (FeatureFlagService sut, _) = Build();

        (await sut.IsEnabledForAsync("nope", Channel)).Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_when_below_the_minimum_tier()
    {
        (FeatureFlagService sut, AuthDbContext db) = Build(tierMet: false);
        FeatureFlag flag = await SeedFlagAsync(db, global: true, rollout: 100);
        flag.MinTierKey = "pro";
        await db.SaveChangesAsync();

        (await sut.IsEnabledForAsync("feat", Channel)).Should().BeFalse();
    }
}
