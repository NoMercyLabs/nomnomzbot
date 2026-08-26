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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing;
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
        BillingTierService tiers = new(db);
        UsageMeteringService metering = new(
            db,
            tiers,
            new RecordingEventBus(),
            new FakeTimeProvider()
        );
        return (new(tiers, metering, db), db);
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

    // ─── S-BUDGETS-b1: the usage report shares ONE count with enforcement ─────

    private static void SeedCommands(AuthDbContext db, Guid broadcasterId, int count)
    {
        for (int i = 0; i < count; i++)
            db.Commands.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    Name = $"cmd{i}",
                    NameNormalized = $"cmd{i}",
                    TemplateResponse = "hi",
                }
            );
    }

    [Fact]
    public async Task GetCurrentCountAsync_matches_the_count_a_write_would_see_at_the_cap()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        // Seed exactly the registry's custom_commands safety baseline (1500).
        SeedCommands(db, Channel, 1500);
        await db.SaveChangesAsync();

        // The read-side count (what the usage report would show)...
        long currentCount = (await sut.GetCurrentCountAsync(Channel, "custom_commands")).Value;
        currentCount.Should().Be(1500);

        // ...is the EXACT count the write path passes into CheckAsync. Proving the two never disagree: the
        // (N+1)th create — evaluated the same way CommandService does — is refused at this same count.
        QuotaCheckDto nextCreate = (
            await sut.CheckAsync(Channel, "custom_commands", currentCount + 1)
        ).Value;
        nextCreate.Allowed.Should().BeFalse();
        nextCreate.Limit.Should().Be(1500);

        // And the report for the resource under evaluation is built from that same GetCurrentCountAsync call —
        // never a second, independently-written counting query.
        ResourceUsageDto report = (await sut.GetUsageReportAsync(Channel)).Value.Single(r =>
            r.LimitKey == "custom_commands"
        );
        report.CurrentCount.Should().Be(currentCount);
        report.Limit.Should().Be(1500);
        report.Class.Should().Be(ResourceClass.NearFree);
    }

    [Fact]
    public async Task GetUsageReportAsync_never_leaks_another_tenants_rows_into_the_count()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        Guid channelA = Channel;
        Guid channelB = Guid.Parse("0192a000-0000-7000-8000-0000000000fa");
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        db.Channels.Add(
            new()
            {
                Id = channelB,
                TwitchChannelId = "t2",
                Name = "chan2",
                NameNormalized = "chan2",
                DeploymentMode = AuthEnums.DeploymentMode.SelfHostFull,
            }
        );
        SeedCommands(db, channelA, 3);
        SeedCommands(db, channelB, 7);
        await db.SaveChangesAsync();

        long countA = (await sut.GetCurrentCountAsync(channelA, "custom_commands")).Value;
        long countB = (await sut.GetCurrentCountAsync(channelB, "custom_commands")).Value;

        countA.Should().Be(3);
        countB.Should().Be(7);
    }

    [Fact]
    public async Task Self_host_usage_report_never_carries_a_commercial_ceiling_for_near_free_resources()
    {
        (ResourceQuotaService sut, AuthDbContext db) = Build();
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        await db.SaveChangesAsync();

        IReadOnlyList<ResourceUsageDto> report = (await sut.GetUsageReportAsync(Channel)).Value;

        ResourceUsageDto customCommands = report.Single(r => r.LimitKey == "custom_commands");
        customCommands.Class.Should().Be(ResourceClass.NearFree);
        // The safety baseline IS the limit — never a tier-scaled, sellable ceiling — on self-host too.
        customCommands.Limit.Should().Be(customCommands.SafetyBaseline);

        ResourceUsageDto tts = report.Single(r => r.LimitKey == "tts_max_characters");
        tts.Class.Should().Be(ResourceClass.CostDriving);
        // -1 = unlimited: self-host reports no paid ceiling at all for the cost-driving resource either.
        tts.Limit.Should().Be(-1);
    }
}
