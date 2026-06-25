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
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Entities;
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

    private static (BillingTierService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new BillingTierService(db), db);
    }

    private static async Task SeedTiersAsync(AuthDbContext db)
    {
        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
    }

    private static void SeedChannel(AuthDbContext db, string deploymentMode)
    {
        db.Channels.Add(
            new Channel
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
            new Subscription
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
            .Contain(l => l.LimitKey == "custom_commands" && l.LimitValue == 100);
    }

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
        (await sut.GetLimitAsync(Channel, "custom_commands")).Value.Should().Be(-1);
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
        entitlement.Limits["custom_commands"].Should().Be(400);
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
        entitlement.Limits["custom_commands"].Should().Be(100);
    }
}
