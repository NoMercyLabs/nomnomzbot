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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// A tier edit reaches flag gating at once: a flag gated on a tier floor answers from its cache for up to a
/// minute, and a tier save that reorders the tiers drops every cached verdict so the next check reads the
/// new order — not the one the cache remembered.
/// </summary>
public sealed class TierEditFlagGatingTests : IDisposable
{
    private static readonly Guid ChannelId = Guid.Parse("0199f000-0000-7000-8000-0000000000c1");
    private const string ProOnlyFlag = "pro-only-feature";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly FeatureFlagCacheService _flagCache = new(
        new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance
        )
    );
    private readonly Guid _baseTierId;

    public TierEditFlagGatingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        BillingTier baseTier = Tier(EffectiveTierRule.BaseTierKey, "Base", sortOrder: 1);
        _baseTierId = baseTier.Id;
        db.BillingTiers.AddRange(baseTier, Tier("pro", "Pro", sortOrder: 2));

        // A hosted channel with no subscription resolves to the base tier.
        db.Channels.Add(
            new Channel
            {
                Id = ChannelId,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "ext-c",
                Name = "channel-c",
                NameNormalized = "channel-c",
            }
        );
        db.FeatureFlags.Add(
            new FeatureFlag
            {
                Key = ProOnlyFlag,
                IsEnabledGlobally = true,
                RolloutPercentage = 100,
                MinTierKey = "pro",
            }
        );
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static BillingTier Tier(string key, string name, int sortOrder) =>
        new()
        {
            Key = key,
            DisplayName = name,
            PriceCents = 0,
            Currency = "usd",
            IsPublic = true,
            SortOrder = sortOrder,
        };

    private async Task<bool> IsFlagOnAsync()
    {
        using AppDbContext db = NewDbContext();
        return await new FeatureFlagService(
            db,
            new CurrentTenantService(),
            _flagCache,
            new BillingTierService(db, _time),
            _time
        ).IsEnabledForAsync(ProOnlyFlag, ChannelId);
    }

    private async Task RankBaseTierAsync(int sortOrder)
    {
        using AppDbContext db = NewDbContext();
        BillingTierAdminService admin = new(db, _time, _flagCache);
        Result<TierChangePreviewDto> preview = await admin.PreviewTierChangeAsync(_baseTierId);
        Result<TierDto> saved = await admin.UpdateTierAsync(
            _baseTierId,
            new UpdateTierRequest(
                "Base",
                0,
                "usd",
                false,
                false,
                true,
                sortOrder,
                [],
                preview.Value.AffectedTenantCount
            ),
            actorUserId: null
        );
        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
    }

    [Fact]
    public async Task RankingATierAboveTheFlagFloor_OpensTheFlagOnTheNextCheck_NotAMinuteLater()
    {
        (await IsFlagOnAsync()).Should().BeFalse("base ranks below the pro floor");

        await RankBaseTierAsync(sortOrder: 3);

        (await IsFlagOnAsync()).Should().BeTrue("the tier edit dropped the cached verdict");
    }

    [Fact]
    public async Task RankingATierBelowTheFlagFloor_ClosesTheFlagOnTheNextCheck()
    {
        await RankBaseTierAsync(sortOrder: 3);
        (await IsFlagOnAsync()).Should().BeTrue();

        await RankBaseTierAsync(sortOrder: 1);

        (await IsFlagOnAsync()).Should().BeFalse("a lowered tier must lose the feature at once");
    }

    [Fact]
    public async Task WithoutATierEdit_TheVerdictIsServedFromTheCache()
    {
        // The control for the two tests above: a direct database change the tier desk never saw is NOT
        // picked up inside the cache lifetime, so the only thing that opens the flag there is the save.
        (await IsFlagOnAsync())
            .Should()
            .BeFalse();
        using (AppDbContext db = NewDbContext())
        {
            db.BillingTiers.Where(t => t.Id == _baseTierId)
                .ExecuteUpdate(s => s.SetProperty(t => t.SortOrder, 3));
        }

        (await IsFlagOnAsync()).Should().BeFalse();
    }
}
