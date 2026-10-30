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
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Content.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the seeded tier catalogue + entitlement resolution (monetization-billing.md §3.2/§8.6): the public
/// catalogue excludes the self-host marker and carries the §8.6 limits; a self-host channel is unlimited; a SaaS
/// channel resolves its active tier (or the base entry tier when unsubscribed); and tier ranking respects order.
/// </summary>
public sealed class BillingTierServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");

    private static (BillingTierService Sut, AuthDbContext Db) Build(
        string defaultTierKey = EffectiveTierRule.BaseTierKey
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new(db, TimeProvider.System, TestTiers.Unsubscribed(defaultTierKey)), db);
    }

    private static async Task SeedTiersAsync(AuthDbContext db)
    {
        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
    }

    private static void SeedChannel(AuthDbContext db, string deploymentMode)
    {
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
    }

    private static async Task SeedSubAsync(AuthDbContext db, string tierKey)
    {
        BillingTier tier = await db.BillingTiers.FirstAsync(t => t.Key == tierKey);
        db.Subscriptions.Add(
            new()
            {
                BroadcasterId = Channel,
                TierId = tier.Id,
                Status = SubscriptionStatus.Active,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Public_catalogue_excludes_the_self_host_marker_and_carries_the_limits()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);

        IReadOnlyList<TierDto> tiers = (await sut.GetPublicTiersAsync()).Value;

        tiers.Select(t => t.Key).Should().ContainInOrder("base", "pro", "premium");
        tiers.Should().NotContain(t => t.Key == "free"); // self-host marker is not public
        tiers
            .First(t => t.Key == "base")
            .Limits.Should()
            .Contain(l => l.LimitKey == "tts_max_characters" && l.LimitValue == 500);
    }

    [Fact]
    public async Task The_seeder_adds_the_free_plan_and_keeps_limits_an_operator_tuned()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedTiersAsync(db);

        BillingTier free = await db.BillingTiers.SingleAsync(t => t.Key == "hosted_free");
        BillingTier baseTier = await db.BillingTiers.SingleAsync(t => t.Key == "base");
        free.DisplayName.Should().Be("Free");
        free.PriceCents.Should().Be(0);
        free.IsPublic.Should().BeTrue();
        free.SortOrder.Should().BeLessThan(baseTier.SortOrder);
        (await LimitsAsync(db, free.Id))
            .Should()
            .Equal(
                new Dictionary<string, long>
                {
                    ["tts_max_characters"] = 200,
                    ["sandbox_exec_ms"] = 0,
                    ["sound_clip_storage_bytes"] = 25L * 1024 * 1024,
                    ["channel_asset_storage_bytes"] = 25L * 1024 * 1024,
                    ["tts_monthly_characters"] = 25_000,
                }
            );
        (await LimitsAsync(db, baseTier.Id))["tts_monthly_characters"].Should().Be(-1);

        TierLimit tunedBase = await db.TierLimits.SingleAsync(l =>
            l.TierId == baseTier.Id && l.LimitKey == "tts_max_characters"
        );
        TierLimit tunedFree = await db.TierLimits.SingleAsync(l =>
            l.TierId == free.Id && l.LimitKey == "tts_monthly_characters"
        );
        tunedBase.LimitValue = 750;
        tunedFree.LimitValue = 30_000;
        await db.SaveChangesAsync();

        await SeedTiersAsync(db);

        (await db.BillingTiers.CountAsync(t => t.Key == "hosted_free")).Should().Be(1);
        (await LimitsAsync(db, baseTier.Id))["tts_max_characters"].Should().Be(750);
        (await LimitsAsync(db, free.Id))["tts_monthly_characters"].Should().Be(30_000);
    }

    private static Task<Dictionary<string, long>> LimitsAsync(AuthDbContext db, Guid tierId) =>
        db
            .TierLimits.AsNoTracking()
            .Where(l => l.TierId == tierId && l.DeletedAt == null)
            .ToDictionaryAsync(l => l.LimitKey, l => l.LimitValue);

    [Fact]
    public async Task A_self_host_channel_is_unlimited()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.SelfHostFull);
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("free");
        entitlement.AllowsCustomBotName.Should().BeTrue();
        entitlement.Limits.Values.Should().AllSatisfy(v => v.Should().Be(-1));
        (await sut.GetLimitAsync(Channel, "tts_max_characters")).Value.Should().Be(-1);
        (await sut.IsTierAtLeastAsync(Channel, "premium")).Value.Should().BeTrue();
    }

    [Fact]
    public async Task A_saas_channel_resolves_its_active_tier()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await db.SaveChangesAsync();
        await SeedSubAsync(db, "pro");

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("pro");
        entitlement.Limits["tts_max_characters"].Should().Be(2000);
        (await sut.IsTierAtLeastAsync(Channel, "base")).Value.Should().BeTrue();
        (await sut.IsTierAtLeastAsync(Channel, "premium")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task A_saas_channel_without_a_subscription_falls_back_to_base()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("base");
        entitlement.Limits["tts_max_characters"].Should().Be(500);
    }

    [Fact]
    public async Task A_channel_without_a_plan_lands_on_the_configured_free_tier_with_its_limits()
    {
        (BillingTierService sut, AuthDbContext db) = Build(defaultTierKey: "hosted_free");
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("hosted_free");
        entitlement.Limits["sandbox_exec_ms"].Should().Be(0);
        entitlement.Limits["tts_max_characters"].Should().Be(200);
        entitlement.Limits["tts_monthly_characters"].Should().Be(25_000);
        (await sut.IsTierAtLeastAsync(Channel, "base")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task A_configured_tier_that_does_not_exist_falls_back_to_base()
    {
        (BillingTierService sut, AuthDbContext db) = Build(defaultTierKey: "no_such_tier");
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("base");
        entitlement.Limits["sandbox_exec_ms"].Should().Be(600_000);
    }

    // A6 admin truth: an operator's per-tenant override is what the TTS cap and the sandbox/TTS metering
    // enforce, because they all read their limit here — not only the create-path quota check.
    [Fact]
    public async Task A_live_tenant_override_replaces_the_tier_limit_for_every_reader()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        AddOverride(db, "tts_max_characters", 4000, expiresAt: DateTime.UtcNow.AddDays(7));
        AddOverride(db, "sandbox_exec_ms", 1000, expiresAt: null);
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.TierKey.Should().Be("base");
        entitlement.Limits["tts_max_characters"].Should().Be(4000);
        entitlement.Limits["sandbox_exec_ms"].Should().Be(1000);
        (await sut.GetLimitAsync(Channel, "tts_max_characters")).Value.Should().Be(4000);

        // The consequence at the enforcer: the sandbox metering check now refuses what the tier would allow.
        UsageMeteringService metering = new(
            db,
            sut,
            new RecordingEventBus(),
            TimeProvider.System,
            TestConfiguration.Empty()
        );
        QuotaCheckDto sandbox = (await metering.CheckAsync(Channel, "sandbox_exec_ms", 5000)).Value;
        sandbox.Allowed.Should().BeFalse();
        sandbox.Limit.Should().Be(1000);
    }

    [Fact]
    public async Task An_expired_or_cleared_tenant_override_no_longer_applies()
    {
        (BillingTierService sut, AuthDbContext db) = Build();
        await SeedTiersAsync(db);
        SeedChannel(db, AuthEnums.DeploymentMode.Saas);
        AddOverride(db, "tts_max_characters", 4000, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        TenantLimitOverride cleared = AddOverride(db, "sandbox_exec_ms", 1000, expiresAt: null);
        cleared.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        EntitlementDto entitlement = (await sut.GetEntitlementAsync(Channel)).Value;

        entitlement.Limits["tts_max_characters"].Should().Be(500);
        entitlement.Limits["sandbox_exec_ms"].Should().Be(600_000);
    }

    private static TenantLimitOverride AddOverride(
        AuthDbContext db,
        string limitKey,
        long value,
        DateTime? expiresAt
    )
    {
        TenantLimitOverride row = new()
        {
            BroadcasterId = Channel,
            LimitKey = limitKey,
            LimitValue = value,
            Reason = "support case",
            ExpiresAt = expiresAt,
        };
        db.TenantLimitOverrides.Add(row);
        return row;
    }
}
