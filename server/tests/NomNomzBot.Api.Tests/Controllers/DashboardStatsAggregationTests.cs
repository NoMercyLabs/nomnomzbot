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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the home screen's supporter-money number is honest: a single-currency day sums to its minor-unit
/// total; a MIXED-currency day reports null (a cross-currency sum is meaningless, and the event count still
/// shows); amount-less events (a TreatStream treat) never invent money and don't block the total of the
/// events that do carry amounts. Also proves <c>platformsLive</c> is owner-scoped: it aggregates the
/// owner's own live platform presences and never leaks another streamer's live state.
/// </summary>
public sealed class DashboardStatsAggregationTests
{
    [Fact]
    public void SingleCurrencyDay_SumsToItsMinorUnitTotal()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (500, "USD"),
            (1337, "usd"),
            (null, null),
        ]);

        amount.Should().Be(1837, "500 + 1337 minor units, case-insensitive same currency");
        currency.Should().Be("USD");
    }

    [Fact]
    public void MixedCurrencyDay_ReportsNull_NeverACrossCurrencySum()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (500, "USD"),
            (500, "EUR"),
        ]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }

    [Fact]
    public void AmountlessDay_ReportsNull_NeverZeroMoney()
    {
        // e.g. only TreatStream treats today — items, not money.
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (null, null),
            (null, null),
        ]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }

    [Fact]
    public void EmptyDay_ReportsNull()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }

    // ─── platformsLive — owner-scoped live aggregation ───

    private static readonly Guid Tenant = Guid.Parse("019a0000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("019a0000-0000-7000-8000-0000000000a9");
    private static readonly Guid OtherOwner = Guid.Parse("019a0000-0000-7000-8000-0000000000b9");

    private static Channel Presence(Guid id, Guid owner, string provider, bool isLive) =>
        new()
        {
            Id = id,
            OwnerUserId = owner,
            Provider = provider,
            ExternalChannelId = id.ToString("N"),
            Name = $"ch-{provider}",
            NameNormalized = $"ch-{provider}",
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
            IsLive = isLive,
        };

    [Fact]
    public async Task PlatformsLive_aggregates_only_the_owners_live_presences()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Channels.AddRange(
            Presence(Tenant, Owner, AuthEnums.Platform.Twitch, isLive: true),
            Presence(Guid.NewGuid(), Owner, AuthEnums.Platform.YouTube, isLive: true),
            Presence(Guid.NewGuid(), Owner, AuthEnums.Platform.Kick, isLive: false),
            // Another streamer live on Kick — must never leak into this owner's answer.
            Presence(Guid.NewGuid(), OtherOwner, AuthEnums.Platform.Kick, isLive: true)
        );
        await db.SaveChangesAsync();

        List<string> live = await DashboardController.ResolvePlatformsLiveAsync(
            db,
            Tenant,
            CancellationToken.None
        );

        live.Should().Equal("twitch", "youtube");
    }

    [Fact]
    public async Task PlatformsLive_is_empty_when_the_owner_is_fully_offline()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Channels.AddRange(
            Presence(Tenant, Owner, AuthEnums.Platform.Twitch, isLive: false),
            Presence(Guid.NewGuid(), Owner, AuthEnums.Platform.YouTube, isLive: false)
        );
        await db.SaveChangesAsync();

        List<string> live = await DashboardController.ResolvePlatformsLiveAsync(
            db,
            Tenant,
            CancellationToken.None
        );

        live.Should().BeEmpty();
    }

    [Fact]
    public async Task PlatformsLive_for_an_unknown_tenant_is_empty_not_a_throw()
    {
        ApiTestDbContext db = ApiTestDbContext.New();

        List<string> live = await DashboardController.ResolvePlatformsLiveAsync(
            db,
            Guid.NewGuid(),
            CancellationToken.None
        );

        live.Should().BeEmpty();
    }
}
