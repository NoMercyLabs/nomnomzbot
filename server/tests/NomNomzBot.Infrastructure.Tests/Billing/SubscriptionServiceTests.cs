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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Content.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the subscription lifecycle core (monetization-billing.md §3.1): an invite/admin grant assigns the tier,
/// syncs <c>Channels.BillingTierKey</c>, and fires the tier-changed event; an unsubscribed read synthesizes the
/// entitlement view; cancel-at-period-end + resume toggle the flag; an inbound Stripe subscription event
/// converges the status; and the outbound Stripe operations are unavailable until the gateway lands.
/// </summary>
public sealed class SubscriptionServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e7");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (SubscriptionService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        SubscriptionService sut = new(
            db,
            new BillingTierService(db),
            bus,
            new FakeTimeProvider(Now)
        );
        return (sut, db, bus);
    }

    private static async Task SeedAsync(
        AuthDbContext db,
        string mode = AuthEnums.DeploymentMode.Saas
    )
    {
        await new BillingTierSeeder(db).SeedAsync();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                TwitchChannelId = "t1",
                Name = "chan",
                NameNormalized = "chan",
                DeploymentMode = mode,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GrantTier_assigns_the_tier_and_syncs_the_channel()
    {
        (SubscriptionService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);
        BillingTier pro = await db.BillingTiers.FirstAsync(t => t.Key == "pro");

        Result<SubscriptionDto> result = await sut.GrantTierAsync(
            Channel,
            pro.Id,
            isInviteOnlyGrant: true
        );

        result.Value.TierKey.Should().Be("pro");
        result.Value.IsInviteOnlyGrant.Should().BeTrue();
        db.Channels.Single(c => c.Id == Channel).BillingTierKey.Should().Be("pro");
        db.Subscriptions.Single(s => s.BroadcasterId == Channel).TierId.Should().Be(pro.Id);
        bus.Published.OfType<SubscriptionTierChangedEvent>()
            .Should()
            .ContainSingle(e => e.ToTierKey == "pro" && e.IsInviteOnlyGrant);
    }

    [Fact]
    public async Task GetSubscription_synthesizes_the_entitlement_view_for_self_host()
    {
        (SubscriptionService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db, AuthEnums.DeploymentMode.SelfHostFull);

        Result<SubscriptionDto> result = await sut.GetSubscriptionAsync(Channel);

        result.Value.TierKey.Should().Be("free");
        result.Value.AllowsCustomBotName.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_at_period_end_then_resume_toggles_the_flag()
    {
        (SubscriptionService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);
        BillingTier pro = await db.BillingTiers.FirstAsync(t => t.Key == "pro");
        await sut.GrantTierAsync(Channel, pro.Id, isInviteOnlyGrant: false);

        Result<SubscriptionDto> canceled = await sut.CancelAsync(
            Channel,
            new CancelSubscriptionRequest(AtPeriodEnd: true, null)
        );
        canceled.Value.CancelAtPeriodEnd.Should().BeTrue();
        bus.Published.OfType<SubscriptionCanceledEvent>()
            .Should()
            .ContainSingle(e => e.AtPeriodEnd);

        Result<SubscriptionDto> resumed = await sut.ResumeAsync(Channel);
        resumed.Value.CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_stripe_subscription_event_converges_the_status()
    {
        (SubscriptionService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);
        BillingTier pro = await db.BillingTiers.FirstAsync(t => t.Key == "pro");
        db.Subscriptions.Add(
            new Subscription
            {
                BroadcasterId = Channel,
                TierId = pro.Id,
                Status = SubscriptionStatus.Incomplete,
                StripeSubscriptionId = "sub_123",
            }
        );
        await db.SaveChangesAsync();

        Result result = await sut.ApplyStripeSubscriptionEventAsync(
            new StripeSubscriptionEventDto(
                "evt_1",
                "customer.subscription.updated",
                "cus_1",
                "sub_123",
                null,
                "active",
                Now.AddDays(-1),
                Now.AddDays(29),
                null,
                false
            )
        );

        result.IsSuccess.Should().BeTrue();
        db.Subscriptions.Single().Status.Should().Be(SubscriptionStatus.Active);
        bus.Published.OfType<SubscriptionStatusChangedEvent>()
            .Should()
            .ContainSingle(e => e.FromStatus == "incomplete" && e.ToStatus == "active");
    }

    [Fact]
    public async Task Stripe_outbound_checkout_is_unavailable_until_the_gateway_lands()
    {
        (SubscriptionService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);

        Result<CheckoutSessionDto> result = await sut.StartCheckoutAsync(
            Channel,
            new StartCheckoutRequest("pro", null, null)
        );

        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }
}
