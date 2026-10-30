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

using System.Globalization;
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
/// S-GIFT-PLAN: an admin gifts one channel any tier (a hidden one too) for N months or forever. The stored
/// row carries the tier and the end date (null for forever), the channel resolves to the gifted tier and its
/// limits at once, a gift ended early stops resolving at once, and a request with an explicit end date keeps
/// working as before. Real services, a real SQLite database; only the clock is faked.
/// </summary>
public sealed class EntitlementGrantGiftTests : IDisposable
{
    private static readonly Guid ChannelId = Guid.Parse("0199f000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0199f000-0000-7000-8000-0000000000d2"
    );
    private static readonly Guid Admin = Guid.Parse("0199f000-0000-7000-8000-0000000000d3");
    private const string LimitKey = "custom_commands";
    private const string GiftedFlag = "gifted-only-feature";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly FeatureFlagCacheService _flagCache = new(
        new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance
        )
    );
    private readonly Guid _giftTierId;

    public EntitlementGrantGiftTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        BillingTier baseTier = Tier(EffectiveTierRule.BaseTierKey, sortOrder: 1, isPublic: true);
        // The gift tier is hidden from the public plan list: an admin can still gift it.
        BillingTier giftTier = Tier("unlimited", sortOrder: 5, isPublic: false);
        _giftTierId = giftTier.Id;
        db.BillingTiers.AddRange(baseTier, giftTier);
        db.TierLimits.AddRange(
            new TierLimit
            {
                TierId = baseTier.Id,
                LimitKey = LimitKey,
                LimitValue = 10,
            },
            new TierLimit
            {
                TierId = giftTier.Id,
                LimitKey = LimitKey,
                LimitValue = 500,
            }
        );
        db.Channels.AddRange(NewChannel(ChannelId, "gifted"), NewChannel(OtherChannelId, "other"));
        db.FeatureFlags.Add(
            new FeatureFlag
            {
                Key = GiftedFlag,
                IsEnabledGlobally = true,
                RolloutPercentage = 100,
                MinTierKey = "unlimited",
            }
        );
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static BillingTier Tier(string key, int sortOrder, bool isPublic) =>
        new()
        {
            Key = key,
            DisplayName = key,
            PriceCents = 0,
            Currency = "usd",
            IsPublic = isPublic,
            SortOrder = sortOrder,
        };

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = $"ext-{name}",
            Name = name,
            NameNormalized = name,
        };

    private EntitlementGrantService NewGrantService(AppDbContext db) =>
        new(db, new BillingTierService(db, _time), _time, _flagCache);

    private async Task<Result<EntitlementGrantDto>> GiftAsync(
        int? months = null,
        bool forever = false,
        DateTime? expiresAt = null
    )
    {
        using AppDbContext db = NewDbContext();
        EntitlementGrantService grants = NewGrantService(db);
        Result<EntitlementGrantPreviewDto> preview = await grants.PreviewGrantAsync(
            ChannelId,
            _giftTierId
        );
        return await grants.IssueGrantAsync(
            ChannelId,
            new IssueEntitlementGrantRequest(
                TierId: _giftTierId,
                Reason: "Tester of the bot",
                ConfirmedChangedLimitCount: preview.Value.ChangedLimitCount,
                ExpiresAt: expiresAt,
                Months: months,
                Forever: forever
            ),
            Admin
        );
    }

    private async Task<EntitlementDto> ResolveAsync(Guid channelId)
    {
        using AppDbContext db = NewDbContext();
        Result<EntitlementDto> entitlement = await new BillingTierService(
            db,
            _time
        ).GetEntitlementAsync(channelId);
        entitlement.IsSuccess.Should().BeTrue(entitlement.ErrorMessage);
        return entitlement.Value;
    }

    private async Task<EntitlementGrant> StoredGrantAsync()
    {
        using AppDbContext db = NewDbContext();
        return await db
            .EntitlementGrants.AsNoTracking()
            .SingleAsync(g => g.BroadcasterId == ChannelId);
    }

    private async Task<IReadOnlyList<EntitlementGrantDto>> ListAsync()
    {
        using AppDbContext db = NewDbContext();
        return (await NewGrantService(db).ListGrantsAsync(ChannelId)).Value;
    }

    private async Task<bool> IsGiftedFlagOnAsync()
    {
        using AppDbContext db = NewDbContext();
        return await new FeatureFlagService(
            db,
            new CurrentTenantService(),
            _flagCache,
            new BillingTierService(db, _time),
            _time
        ).IsEnabledForAsync(GiftedFlag, ChannelId);
    }

    [Fact]
    public async Task A_forever_gift_stores_no_end_date_and_keeps_the_tier_for_good()
    {
        Result<EntitlementGrantDto> issued = await GiftAsync(forever: true);

        issued.IsSuccess.Should().BeTrue(issued.ErrorMessage);
        issued.Value.ExpiresAt.Should().BeNull();
        EntitlementGrant stored = await StoredGrantAsync();
        stored.GrantedTierId.Should().Be(_giftTierId);
        stored.ExpiresAt.Should().BeNull("a forever gift has no end date");
        stored.IssuedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        stored.IssuedByAdminId.Should().Be(Admin);

        EntitlementDto now = await ResolveAsync(ChannelId);
        now.TierKey.Should().Be("unlimited");
        now.Limits[LimitKey].Should().Be(500);

        _time.Advance(TimeSpan.FromDays(365 * 30));
        EntitlementDto later = await ResolveAsync(ChannelId);
        later.TierKey.Should().Be("unlimited", "a forever gift never lapses");
        later.Limits[LimitKey].Should().Be(500);

        IReadOnlyList<EntitlementGrantDto> listed = await ListAsync();
        listed.Should().ContainSingle().Which.ExpiresAt.Should().BeNull();
        (await ResolveAsync(OtherChannelId))
            .TierKey.Should()
            .Be(EffectiveTierRule.BaseTierKey, "the gift is for one channel only");
    }

    [Fact]
    public async Task A_gift_of_N_months_ends_after_N_months_and_the_tier_falls_back()
    {
        Result<EntitlementGrantDto> issued = await GiftAsync(months: 3);

        issued.IsSuccess.Should().BeTrue(issued.ErrorMessage);
        DateTime expectedEnd = new(2027, 1, 29, 12, 0, 0, DateTimeKind.Utc);
        issued.Value.ExpiresAt.Should().Be(expectedEnd);
        EntitlementGrant stored = await StoredGrantAsync();
        stored.GrantedTierId.Should().Be(_giftTierId);
        stored.ExpiresAt.Should().Be(expectedEnd);

        EntitlementDto during = await ResolveAsync(ChannelId);
        during.TierKey.Should().Be("unlimited");
        during.Limits[LimitKey].Should().Be(500);

        _time.SetUtcNow(new DateTimeOffset(expectedEnd).AddMinutes(-1));
        (await ResolveAsync(ChannelId)).TierKey.Should().Be("unlimited");

        _time.SetUtcNow(new DateTimeOffset(expectedEnd).AddMinutes(1));
        EntitlementDto after = await ResolveAsync(ChannelId);
        after.TierKey.Should().Be(EffectiveTierRule.BaseTierKey);
        after.Limits[LimitKey].Should().Be(10, "the base limit is back once the gift ends");
        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_gift_opens_a_tier_gated_flag_on_the_next_check()
    {
        (await IsGiftedFlagOnAsync()).Should().BeFalse("base ranks below the flag's floor");

        (await GiftAsync(forever: true)).IsSuccess.Should().BeTrue();

        (await IsGiftedFlagOnAsync())
            .Should()
            .BeTrue("the gift changes the tier at once, not after the flag cache expires");
    }

    [Fact]
    public async Task Ending_a_gift_early_sets_the_end_date_to_now_and_the_tier_resolves_back_at_once()
    {
        Result<EntitlementGrantDto> issued = await GiftAsync(forever: true);
        (await IsGiftedFlagOnAsync()).Should().BeTrue();
        _time.Advance(TimeSpan.FromDays(10));
        DateTime endedAt = _time.GetUtcNow().UtcDateTime;

        Result<EntitlementGrantDto> ended;
        using (AppDbContext db = NewDbContext())
        {
            ended = await NewGrantService(db).EndGrantAsync(ChannelId, issued.Value.Id, Admin);
        }

        ended.IsSuccess.Should().BeTrue(ended.ErrorMessage);
        ended.Value.ExpiresAt.Should().Be(endedAt);
        EntitlementGrant stored = await StoredGrantAsync();
        stored.ExpiresAt.Should().Be(endedAt, "the row stays as the record, ended now");
        stored.DeletedAt.Should().BeNull();

        EntitlementDto after = await ResolveAsync(ChannelId);
        after.TierKey.Should().Be(EffectiveTierRule.BaseTierKey);
        after.Limits[LimitKey].Should().Be(10);
        (await ListAsync()).Should().BeEmpty();
        (await IsGiftedFlagOnAsync()).Should().BeFalse("ending the gift drops the cached verdict");

        using AppDbContext auditDb = NewDbContext();
        IamAuditLog audit = await auditDb
            .IamAuditLogs.AsNoTracking()
            .SingleAsync(a => a.Permission == "entitlement_grant:end");
        audit.PrincipalId.Should().Be(Admin);
        audit.TargetBroadcasterId.Should().Be(ChannelId);
        audit.TargetResource.Should().Be("unlimited");
    }

    [Fact]
    public async Task Ending_a_gift_through_another_channel_is_refused_and_changes_nothing()
    {
        Result<EntitlementGrantDto> issued = await GiftAsync(months: 2);

        Result<EntitlementGrantDto> ended;
        using (AppDbContext db = NewDbContext())
        {
            ended = await NewGrantService(db).EndGrantAsync(OtherChannelId, issued.Value.Id, Admin);
        }

        ended.IsFailure.Should().BeTrue();
        ended.ErrorCode.Should().Be("NOT_FOUND");
        (await StoredGrantAsync()).ExpiresAt.Should().Be(issued.Value.ExpiresAt);
        (await ResolveAsync(ChannelId)).TierKey.Should().Be("unlimited");
    }

    [Fact]
    public async Task A_request_with_an_explicit_end_date_still_works_as_before()
    {
        DateTime end = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        Result<EntitlementGrantDto> issued = await GiftAsync(expiresAt: end);

        issued.IsSuccess.Should().BeTrue(issued.ErrorMessage);
        (await StoredGrantAsync()).ExpiresAt.Should().Be(end);
        (await ResolveAsync(ChannelId)).TierKey.Should().Be("unlimited");
    }

    [Theory]
    [InlineData(null, false, null, "no length at all")]
    [InlineData(3, true, null, "months and forever together")]
    [InlineData(null, true, "2026-12-01T00:00:00Z", "an end date and forever together")]
    [InlineData(2, false, "2026-12-01T00:00:00Z", "an end date and months together")]
    [InlineData(0, false, null, "zero months")]
    [InlineData(-2, false, null, "a negative number of months")]
    [InlineData(null, false, "2026-01-01T00:00:00Z", "an end date in the past")]
    public async Task A_gift_without_exactly_one_valid_length_is_refused_and_nothing_is_stored(
        int? months,
        bool forever,
        string? endDate,
        string why
    )
    {
        DateTime? end = endDate is null
            ? null
            : DateTimeOffset.Parse(endDate, CultureInfo.InvariantCulture).UtcDateTime;

        Result<EntitlementGrantDto> issued = await GiftAsync(months, forever, end);

        issued.IsFailure.Should().BeTrue(why);
        issued.ErrorCode.Should().Be("VALIDATION_FAILED");
        using AppDbContext db = NewDbContext();
        (await db.EntitlementGrants.AnyAsync()).Should().BeFalse(why);
    }
}
