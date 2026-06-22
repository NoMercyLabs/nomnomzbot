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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Content.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves invite codes + founders badge (monetization-billing.md §3.4): redemption grants the badge + tier
/// (through the subscription service), fires the events, and is single-use per tenant; an exhausted code is
/// rate-limited; validation previews the grants; create generates a code; and the admin path grants a badge.
/// </summary>
public sealed class InviteCodeServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e9");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (InviteCodeService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        SubscriptionService subs = new(
            db,
            new BillingTierService(db),
            Substitute.For<IStripeGateway>(),
            Substitute.For<IConfiguration>(),
            bus,
            new FakeTimeProvider(Now)
        );
        InviteCodeService sut = new(db, subs, bus, new FakeTimeProvider(Now));
        return (sut, db, bus);
    }

    private static async Task SeedAsync(AuthDbContext db)
    {
        await new BillingTierSeeder(db).SeedAsync();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                TwitchChannelId = "t1",
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Redeem_grants_the_badge_and_tier_then_blocks_a_second_redemption()
    {
        (InviteCodeService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);
        BillingTier pro = await db.BillingTiers.FirstAsync(t => t.Key == "pro");
        db.InviteCodes.Add(
            new InviteCode
            {
                Code = "FOUNDER1",
                MaxRedemptions = 5,
                GrantsFoundersBadge = true,
                GrantsTierId = pro.Id,
            }
        );
        await db.SaveChangesAsync();

        Result<RedeemInviteCodeResultDto> redeem = await sut.RedeemAsync(Channel, "FOUNDER1");

        redeem.Value.GrantedFoundersBadge.Should().BeTrue();
        redeem.Value.GrantedTierKey.Should().Be("pro");
        db.FoundersBadges.Should().ContainSingle(b => b.BroadcasterId == Channel);
        db.Subscriptions.Single(s => s.BroadcasterId == Channel).TierId.Should().Be(pro.Id);
        db.InviteCodes.Single().RedemptionCount.Should().Be(1);
        bus.Published.OfType<InviteCodeRedeemedEvent>().Should().ContainSingle();
        bus.Published.OfType<FoundersBadgeGrantedEvent>().Should().ContainSingle();

        (await sut.RedeemAsync(Channel, "FOUNDER1")).ErrorCode.Should().Be("ALREADY_EXISTS");
    }

    [Fact]
    public async Task Validate_previews_the_grants_and_rejects_an_unknown_code()
    {
        (InviteCodeService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);
        BillingTier pro = await db.BillingTiers.FirstAsync(t => t.Key == "pro");
        db.InviteCodes.Add(
            new InviteCode
            {
                Code = "PREVIEW",
                MaxRedemptions = 3,
                GrantsFoundersBadge = true,
                GrantsTierId = pro.Id,
            }
        );
        await db.SaveChangesAsync();

        InviteCodeValidationDto valid = (await sut.ValidateAsync("PREVIEW")).Value;
        valid.IsValid.Should().BeTrue();
        valid.GrantsFoundersBadge.Should().BeTrue();
        valid.GrantsTierKey.Should().Be("pro");
        valid.RemainingRedemptions.Should().Be(3);

        (await sut.ValidateAsync("NOPE")).ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Redeem_is_rate_limited_when_exhausted()
    {
        (InviteCodeService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);
        db.InviteCodes.Add(
            new InviteCode
            {
                Code = "ONCE",
                MaxRedemptions = 1,
                RedemptionCount = 1,
                GrantsFoundersBadge = true,
            }
        );
        await db.SaveChangesAsync();

        (await sut.RedeemAsync(Channel, "ONCE")).ErrorCode.Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task CreateInviteCode_generates_a_code()
    {
        (InviteCodeService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);

        Result<InviteCodeDto> created = await sut.CreateInviteCodeAsync(
            new CreateInviteCodeRequest(MaxRedemptions: 10, GrantsFoundersBadge: true, null, null)
        );

        created.Value.Code.Should().NotBeNullOrEmpty();
        created.Value.MaxRedemptions.Should().Be(10);
        db.InviteCodes.Should().ContainSingle(c => c.Code == created.Value.Code);
    }

    [Fact]
    public async Task GrantFoundersBadge_grants_directly_and_is_idempotent()
    {
        (InviteCodeService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);

        Result<FoundersBadgeDto> first = await sut.GrantFoundersBadgeAsync(Channel);
        await sut.GrantFoundersBadgeAsync(Channel); // idempotent

        first.Value.IsActive.Should().BeTrue();
        db.FoundersBadges.Should().ContainSingle(b => b.BroadcasterId == Channel);
        bus.Published.OfType<FoundersBadgeGrantedEvent>().Should().ContainSingle();
        (await sut.GetFoundersBadgeAsync(Channel)).Value.Should().NotBeNull();
    }
}
