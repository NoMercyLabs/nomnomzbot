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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Content.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the S-BUDGETS-a write-path seam (<see cref="ResourceQuotaService"/>): a NEAR_FREE resource is capped
/// at the registry's own safety baseline for EVERY tenant, self-host included — never tier-scaled, never a
/// paid ceiling — while a COST_DRIVING resource still resolves self-host to unlimited and a SaaS tenant to its
/// real tier limit, unchanged from <see cref="IBillingTierService"/>'s existing behavior.
/// </summary>
public sealed class ResourceQuotaServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f9");

    private static (ResourceQuotaService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new(new BillingTierService(db)), db);
    }

    private static void SeedChannel(AuthDbContext db, string deploymentMode) =>
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "t1",
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = deploymentMode,
            }
        );

    // ─── NEAR_FREE: uniform safety baseline, self-host included ────────────────

    [Fact]
    public async Task NearFree_resource_refuses_the_Nplus1th_row_on_self_host()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        await db.SaveChangesAsync();

        // The registry's own custom_commands safety baseline (1500) — self-host is never crippled below it...
        QuotaCheckDto atBaseline = (await sut.CheckAsync(Channel, "custom_commands", 1500)).Value;
        atBaseline.Allowed.Should().BeTrue();

        // ...but self-host is ALSO never granted a commercial ceiling above it: the (N+1)th row is refused.
        QuotaCheckDto overBaseline = (await sut.CheckAsync(Channel, "custom_commands", 1501)).Value;
        overBaseline.Allowed.Should().BeFalse();
        overBaseline.Limit.Should().Be(1500);
    }

    [Fact]
    public async Task NearFree_resource_applies_the_same_baseline_to_a_saas_tenant_regardless_of_tier()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
        // Subscribe to the CHEAPEST tier, which seeds custom_commands=100 — a paid ceiling the near-free
        // seam must ignore entirely.
        BillingTier baseTier = await db.BillingTiers.FirstAsync(t => t.Key == "base");
        db.Subscriptions.Add(
            new()
            {
                BroadcasterId = Channel,
                TierId = baseTier.Id,
                Status = SubscriptionStatus.Active,
            }
        );
        await db.SaveChangesAsync();

        // 1200 exceeds the base tier's TierLimit (100) but is still under the registry's uniform safety
        // baseline (1500) — proving the near-free key is not gated by the tier's TierLimit row at all.
        QuotaCheckDto check = (await sut.CheckAsync(Channel, "custom_commands", 1200)).Value;

        check.Allowed.Should().BeTrue();
        check.Limit.Should().Be(1500);
    }

    // ─── COST_DRIVING: self-host unlimited, SaaS tier-scaled — unchanged ───────

    [Fact]
    public async Task CostDriving_resource_is_unlimited_on_self_host()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        await db.SaveChangesAsync();

        QuotaCheckDto check = (await sut.CheckAsync(Channel, "tts_max_characters", 999_999)).Value;

        check.Allowed.Should().BeTrue();
        check.Limit.Should().Be(-1);
    }

    [Fact]
    public async Task CostDriving_resource_is_tier_scaled_on_saas()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
        BillingTier baseTier = await db.BillingTiers.FirstAsync(t => t.Key == "base");
        db.Subscriptions.Add(
            new()
            {
                BroadcasterId = Channel,
                TierId = baseTier.Id,
                Status = SubscriptionStatus.Active,
            }
        );
        await db.SaveChangesAsync();

        QuotaCheckDto underCap = (await sut.CheckAsync(Channel, "tts_max_characters", 500)).Value;
        QuotaCheckDto overCap = (await sut.CheckAsync(Channel, "tts_max_characters", 501)).Value;

        underCap.Allowed.Should().BeTrue();
        overCap.Allowed.Should().BeFalse();
        overCap.Limit.Should().Be(500); // base tier's seeded tts_max_characters
    }

    // ─── truthful usage: unknown key refuses loud, never silently allows ───────

    [Fact]
    public async Task An_undeclared_limit_key_fails_rather_than_silently_allowing()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        await db.SaveChangesAsync();

        Application.Common.Models.Result<QuotaCheckDto> result = await sut.CheckAsync(
            Channel,
            "not_a_declared_resource",
            1
        );

        result.IsFailure.Should().BeTrue();
    }
}
