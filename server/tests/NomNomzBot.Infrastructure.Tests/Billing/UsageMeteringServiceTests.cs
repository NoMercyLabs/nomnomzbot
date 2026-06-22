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
/// Proves cost-driver metering + quota enforcement (monetization-billing.md §3.3): a record accumulates into the
/// current-period counter and fires <c>UsageQuotaExceededEvent</c> once on crossing; a check reports remaining
/// without incrementing; self-host never meters; and a non-positive quantity is rejected.
/// </summary>
public sealed class UsageMeteringServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e5");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Metric = "api_calls";

    private static (UsageMeteringService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        UsageMeteringService sut = new(
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
        await db.SaveChangesAsync();
        BillingTier baseTier = await db.BillingTiers.FirstAsync(t => t.Key == "base");
        db.TierLimits.Add(
            new TierLimit
            {
                TierId = baseTier.Id,
                LimitKey = Metric,
                LimitValue = 100,
            }
        );
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
        if (mode == AuthEnums.DeploymentMode.Saas)
            db.Subscriptions.Add(
                new Subscription
                {
                    BroadcasterId = Channel,
                    TierId = baseTier.Id,
                    Status = SubscriptionStatus.Active,
                }
            );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Record_accumulates_and_fires_the_quota_event_once_on_crossing()
    {
        (UsageMeteringService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedAsync(db);

        (await sut.RecordAsync(Channel, Metric, 60)).IsSuccess.Should().BeTrue();
        bus.Published.OfType<UsageQuotaExceededEvent>().Should().BeEmpty(); // 60 < 100

        await sut.RecordAsync(Channel, Metric, 50); // 110 >= 100 — crosses
        bus.Published.OfType<UsageQuotaExceededEvent>()
            .Should()
            .ContainSingle(e => e.Used == 110 && e.Limit == 100);
        db.UsageRecords.Single(u => u.MetricKey == Metric).Quantity.Should().Be(110);
    }

    [Fact]
    public async Task Check_reports_remaining_without_incrementing()
    {
        (UsageMeteringService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);
        await sut.RecordAsync(Channel, Metric, 30);

        QuotaCheckDto within = (await sut.CheckAsync(Channel, Metric, 10)).Value;
        within.Used.Should().Be(30);
        within.Limit.Should().Be(100);
        within.Remaining.Should().Be(70);
        within.Allowed.Should().BeTrue();

        (await sut.CheckAsync(Channel, Metric, 100)).Value.Allowed.Should().BeFalse(); // 30 + 100 > 100
        db.UsageRecords.Single(u => u.MetricKey == Metric).Quantity.Should().Be(30); // unchanged
    }

    [Fact]
    public async Task Record_is_a_noop_on_self_host()
    {
        (UsageMeteringService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db, AuthEnums.DeploymentMode.SelfHostFull);

        (await sut.RecordAsync(Channel, Metric, 999)).IsSuccess.Should().BeTrue();

        db.UsageRecords.Should().BeEmpty(); // self-host is never metered
    }

    [Fact]
    public async Task Record_rejects_a_non_positive_quantity()
    {
        (UsageMeteringService sut, AuthDbContext db, _) = Build();
        await SeedAsync(db);

        (await sut.RecordAsync(Channel, Metric, 0)).ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
