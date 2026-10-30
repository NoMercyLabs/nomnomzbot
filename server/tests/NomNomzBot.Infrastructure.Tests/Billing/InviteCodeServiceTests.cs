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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Billing.EventHandlers;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Invite codes (monetization-billing.md §3.4, S-INVITE-TIER): an invite carries a tier and a length (N months,
/// or forever when no length is set), and redeeming it gives the channel the same gift an admin gives
/// (S-GIFT-PLAN) — an <see cref="EntitlementGrant"/> through the real <see cref="EntitlementGrantService"/>,
/// never a subscription row. A channel redeems one code once; an exhausted or expired code is refused with the
/// same plain reasons as before; the public read tells a signed-out visitor what a link gives and answers an
/// unknown code exactly like an unusable one; and a new streamer's pending code redeems on the channel they own.
/// Real services over a real SQLite database; only the clock is faked.
/// </summary>
public sealed class InviteCodeServiceTests : IDisposable
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000000e9");
    private static readonly Guid OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000ea");
    private const string LimitKey = "custom_commands";

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
    private readonly RecordingEventBus _bus = new();
    private readonly Guid _giftTierId;

    public InviteCodeServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        BillingTier baseTier = Tier(EffectiveTierRule.BaseTierKey, "Base", sortOrder: 1, true);
        // A hidden tier: an invite can hand it out although it is not on the public plan list.
        BillingTier giftTier = Tier("unlimited", "Unlimited", sortOrder: 5, isPublic: false);
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
        db.Channels.Add(
            new Channel
            {
                Id = ChannelId,
                OwnerUserId = OwnerUserId,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "ext-invited",
                Name = "invited",
                NameNormalized = "invited",
                IsOnboarded = true,
            }
        );
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static BillingTier Tier(string key, string name, int sortOrder, bool isPublic) =>
        new()
        {
            Key = key,
            DisplayName = name,
            PriceCents = 0,
            Currency = "usd",
            IsPublic = isPublic,
            SortOrder = sortOrder,
        };

    private InviteCodeService NewService(AppDbContext db) => new(db, NewGrants(db), _bus, _time);

    private EntitlementGrantService NewGrants(AppDbContext db) =>
        new(db, new BillingTierService(db, _time, TestTiers.Unsubscribed()), _time, _flagCache);

    private async Task SignUpAsync(int trialDays)
    {
        using AppDbContext db = NewDbContext();
        await new SignUpTrialOnRegistrationHandler(
            NewGrants(db),
            Options.Create(new BillingOptions { SignUpTrialDays = trialDays }),
            NullLogger<SignUpTrialOnRegistrationHandler>.Instance
        ).HandleAsync(
            new UserRegisteredEvent
            {
                BroadcasterId = ChannelId,
                UserId = OwnerUserId,
                TwitchUserId = "ext-invited",
                Username = "invited",
                Platform = AuthEnums.Platform.Twitch,
            }
        );
    }

    private async Task<InviteCode> AddInviteAsync(
        string code,
        int? months,
        int maxRedemptions = 5,
        int redemptionCount = 0,
        DateTime? expiresAt = null,
        bool foundersBadge = false,
        bool grantsTier = true
    )
    {
        using AppDbContext db = NewDbContext();
        InviteCode invite = new()
        {
            Code = code,
            MaxRedemptions = maxRedemptions,
            RedemptionCount = redemptionCount,
            GrantsFoundersBadge = foundersBadge,
            GrantsTierId = grantsTier ? _giftTierId : null,
            GrantsMonths = months,
            ExpiresAt = expiresAt,
        };
        db.InviteCodes.Add(invite);
        await db.SaveChangesAsync();
        return invite;
    }

    private async Task<Result<RedeemInviteCodeResultDto>> RedeemAsync(string code)
    {
        using AppDbContext db = NewDbContext();
        return await NewService(db).RedeemAsync(ChannelId, code);
    }

    private async Task<Result<RedeemInviteCodeResultDto>> RedeemForOwnerAsync(
        Guid userId,
        string code
    )
    {
        using AppDbContext db = NewDbContext();
        return await NewService(db).RedeemForOwnerAsync(userId, code);
    }

    private async Task<PublicInviteDto> PublicInfoAsync(string code)
    {
        using AppDbContext db = NewDbContext();
        Result<PublicInviteDto> info = await NewService(db).GetPublicInviteAsync(code);
        info.IsSuccess.Should().BeTrue(info.ErrorMessage);
        return info.Value;
    }

    private async Task<Result<InviteCodeDto>> CreateAsync(CreateInviteCodeRequest request)
    {
        using AppDbContext db = NewDbContext();
        return await NewService(db).CreateInviteCodeAsync(request);
    }

    private async Task<EntitlementDto> ResolveAsync()
    {
        using AppDbContext db = NewDbContext();
        Result<EntitlementDto> entitlement = await new BillingTierService(
            db,
            _time,
            TestTiers.Unsubscribed()
        ).GetEntitlementAsync(ChannelId);
        entitlement.IsSuccess.Should().BeTrue(entitlement.ErrorMessage);
        return entitlement.Value;
    }

    private async Task<List<EntitlementGrant>> StoredGrantsAsync()
    {
        using AppDbContext db = NewDbContext();
        return await db.EntitlementGrants.AsNoTracking().ToListAsync();
    }

    private async Task<InviteCode> StoredInviteAsync(string code)
    {
        using AppDbContext db = NewDbContext();
        return await db.InviteCodes.AsNoTracking().SingleAsync(c => c.Code == code);
    }

    [Fact]
    public async Task Create_stores_the_length_beside_the_tier_and_returns_it()
    {
        Result<InviteCodeDto> months = await CreateAsync(
            new(MaxRedemptions: 3, GrantsFoundersBadge: false, _giftTierId, null, GrantsMonths: 6)
        );
        Result<InviteCodeDto> forever = await CreateAsync(
            new(MaxRedemptions: 1, GrantsFoundersBadge: false, _giftTierId, null)
        );

        months.IsSuccess.Should().BeTrue(months.ErrorMessage);
        months.Value.GrantsMonths.Should().Be(6);
        months.Value.GrantsTierKey.Should().Be("unlimited");
        (await StoredInviteAsync(months.Value.Code)).GrantsMonths.Should().Be(6);

        forever.IsSuccess.Should().BeTrue(forever.ErrorMessage);
        forever.Value.GrantsMonths.Should().BeNull("no length with a tier is a forever invite");
        InviteCode storedForever = await StoredInviteAsync(forever.Value.Code);
        storedForever.GrantsMonths.Should().BeNull();
        storedForever.GrantsTierId.Should().Be(_giftTierId);
        storedForever.RedemptionCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, true, "zero months")]
    [InlineData(121, true, "more than 120 months")]
    [InlineData(3, false, "a length without a tier")]
    public async Task Create_refuses_a_bad_length_and_stores_nothing(
        int months,
        bool withTier,
        string why
    )
    {
        Result<InviteCodeDto> created = await CreateAsync(
            new(
                MaxRedemptions: 1,
                GrantsFoundersBadge: true,
                withTier ? _giftTierId : null,
                null,
                GrantsMonths: months
            )
        );

        created.IsFailure.Should().BeTrue(why);
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        using AppDbContext db = NewDbContext();
        (await db.InviteCodes.AnyAsync()).Should().BeFalse(why);
    }

    [Fact]
    public async Task Redeeming_a_months_invite_gives_a_gift_that_ends_after_N_months()
    {
        InviteCode invite = await AddInviteAsync("MONTHS3", months: 3);

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemAsync("MONTHS3");

        redeemed.IsSuccess.Should().BeTrue(redeemed.ErrorMessage);
        redeemed.Value.GrantedTierKey.Should().Be("unlimited");
        DateTime expectedEnd = new(2027, 1, 29, 12, 0, 0, DateTimeKind.Utc);
        redeemed.Value.GrantedTierExpiresAt.Should().Be(expectedEnd);

        EntitlementGrant grant = (await StoredGrantsAsync()).Should().ContainSingle().Subject;
        grant.BroadcasterId.Should().Be(ChannelId);
        grant.GrantedTierId.Should().Be(_giftTierId);
        grant.ExpiresAt.Should().Be(expectedEnd);
        grant.InviteCodeId.Should().Be(invite.Id);
        grant.Reason.Should().Contain("MONTHS3");
        grant.IssuedByAdminId.Should().BeNull("an invite redemption has no admin behind it");
        (await StoredInviteAsync("MONTHS3")).RedemptionCount.Should().Be(1);

        using (AppDbContext db = NewDbContext())
            (await db.Subscriptions.AnyAsync())
                .Should()
                .BeFalse("an invite gives a gift, never a subscription row");

        EntitlementDto during = await ResolveAsync();
        during.TierKey.Should().Be("unlimited");
        during.Limits[LimitKey].Should().Be(500);

        _time.SetUtcNow(new DateTimeOffset(expectedEnd).AddMinutes(1));
        EntitlementDto after = await ResolveAsync();
        after.TierKey.Should().Be(EffectiveTierRule.BaseTierKey);
        after.Limits[LimitKey].Should().Be(10);

        _bus.Published.OfType<InviteCodeRedeemedEvent>()
            .Should()
            .ContainSingle()
            .Which.GrantedTierId.Should()
            .Be(_giftTierId);
    }

    [Fact]
    public async Task An_old_invite_with_a_tier_and_no_length_still_gives_the_tier_for_good()
    {
        await AddInviteAsync("FOREVER", months: null);

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemAsync("FOREVER");

        redeemed.IsSuccess.Should().BeTrue(redeemed.ErrorMessage);
        redeemed.Value.GrantedTierExpiresAt.Should().BeNull();
        EntitlementGrant grant = (await StoredGrantsAsync()).Should().ContainSingle().Subject;
        grant.ExpiresAt.Should().BeNull("a forever gift has no end date");

        _time.Advance(TimeSpan.FromDays(365 * 30));
        (await ResolveAsync()).TierKey.Should().Be("unlimited", "a forever invite never lapses");
    }

    [Fact]
    public async Task A_channel_redeems_one_code_once_and_the_second_try_changes_nothing()
    {
        await AddInviteAsync("TWICE", months: 2, maxRedemptions: 10);
        (await RedeemAsync("TWICE")).IsSuccess.Should().BeTrue();

        Result<RedeemInviteCodeResultDto> second = await RedeemAsync("TWICE");

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("ALREADY_EXISTS");
        second.ErrorMessage.Should().Be("You have already redeemed this code.");
        (await StoredGrantsAsync()).Should().ContainSingle();
        (await StoredInviteAsync("TWICE")).RedemptionCount.Should().Be(1);
    }

    [Fact]
    public async Task An_exhausted_code_is_refused_with_the_plain_reason_and_gives_nothing()
    {
        await AddInviteAsync("USEDUP", months: 1, maxRedemptions: 1, redemptionCount: 1);

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemAsync("USEDUP");

        redeemed.ErrorCode.Should().Be("RATE_LIMITED");
        redeemed.ErrorMessage.Should().Be("Invite code is exhausted.");
        (await StoredGrantsAsync()).Should().BeEmpty();
        (await StoredInviteAsync("USEDUP")).RedemptionCount.Should().Be(1);
    }

    [Fact]
    public async Task An_expired_code_is_refused_with_the_plain_reason_and_gives_nothing()
    {
        await AddInviteAsync(
            "LATE",
            months: 1,
            expiresAt: _time.GetUtcNow().UtcDateTime.AddMinutes(-1)
        );

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemAsync("LATE");

        redeemed.ErrorCode.Should().Be("VALIDATION_FAILED");
        redeemed.ErrorMessage.Should().Be("Invite code has expired.");
        (await StoredGrantsAsync()).Should().BeEmpty();
        (await StoredInviteAsync("LATE")).RedemptionCount.Should().Be(0);
    }

    [Fact]
    public async Task A_badge_and_tier_invite_gives_both_and_fires_both_events()
    {
        await AddInviteAsync("FOUNDER1", months: null, foundersBadge: true);

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemAsync("FOUNDER1");

        redeemed.Value.GrantedFoundersBadge.Should().BeTrue();
        redeemed.Value.GrantedTierKey.Should().Be("unlimited");
        using (AppDbContext db = NewDbContext())
            (await db.FoundersBadges.AsNoTracking().ToListAsync())
                .Should()
                .ContainSingle(b => b.BroadcasterId == ChannelId && b.InviteCode == "FOUNDER1");
        (await StoredGrantsAsync()).Should().ContainSingle();
        _bus.Published.OfType<InviteCodeRedeemedEvent>().Should().ContainSingle();
        _bus.Published.OfType<FoundersBadgeGrantedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task The_public_read_tells_a_visitor_what_a_usable_invite_gives()
    {
        await AddInviteAsync("SHOWME", months: 6);
        await AddInviteAsync("SHOWALL", months: null);

        PublicInviteDto months = await PublicInfoAsync("SHOWME");
        PublicInviteDto forever = await PublicInfoAsync("SHOWALL");

        months.IsUsable.Should().BeTrue();
        months.TierDisplayName.Should().Be("Unlimited");
        months.Months.Should().Be(6);
        months.Forever.Should().BeFalse();
        months.NotUsableReason.Should().BeNull();

        forever.IsUsable.Should().BeTrue();
        forever.TierDisplayName.Should().Be("Unlimited");
        forever.Months.Should().BeNull();
        forever.Forever.Should().BeTrue();
        (await StoredInviteAsync("SHOWME"))
            .RedemptionCount.Should()
            .Be(0, "reading an invite never spends it");
    }

    [Fact]
    public async Task The_public_read_answers_an_unknown_code_exactly_like_an_unusable_one()
    {
        await AddInviteAsync("GONE", months: 1, maxRedemptions: 1, redemptionCount: 1);
        await AddInviteAsync(
            "OVER",
            months: 1,
            expiresAt: _time.GetUtcNow().UtcDateTime.AddDays(-1)
        );

        PublicInviteDto unknown = await PublicInfoAsync("NOSUCHCODE");
        PublicInviteDto usedUp = await PublicInfoAsync("GONE");
        PublicInviteDto expired = await PublicInfoAsync("OVER");

        unknown.IsUsable.Should().BeFalse();
        unknown.NotUsableReason.Should().NotBeNullOrWhiteSpace();
        unknown.TierDisplayName.Should().BeNull();
        unknown.Months.Should().BeNull();
        unknown.Forever.Should().BeFalse();
        usedUp.Should().Be(unknown, "a used-up code must not tell a guesser it exists");
        expired.Should().Be(unknown, "an expired code must not tell a guesser it exists");
    }

    [Fact]
    public async Task A_new_streamers_pending_code_redeems_on_the_channel_they_own_once_only()
    {
        InviteCode invite = await AddInviteAsync("SIGNUP", months: 12, maxRedemptions: 10);

        Result<RedeemInviteCodeResultDto> first = await RedeemForOwnerAsync(OwnerUserId, "SIGNUP");
        Result<RedeemInviteCodeResultDto> again = await RedeemForOwnerAsync(OwnerUserId, "SIGNUP");

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value.GrantedTierKey.Should().Be("unlimited");
        EntitlementGrant grant = (await StoredGrantsAsync()).Should().ContainSingle().Subject;
        grant.BroadcasterId.Should().Be(ChannelId);
        grant.InviteCodeId.Should().Be(invite.Id);
        grant.ExpiresAt.Should().Be(new DateTime(2027, 10, 29, 12, 0, 0, DateTimeKind.Utc));
        (await ResolveAsync()).TierKey.Should().Be("unlimited");

        again.ErrorCode.Should().Be("ALREADY_EXISTS");
        (await StoredInviteAsync("SIGNUP")).RedemptionCount.Should().Be(1);
        (await StoredGrantsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_pending_code_for_a_person_without_a_channel_fails_and_spends_nothing()
    {
        await AddInviteAsync("NOCHAN", months: 1);

        Result<RedeemInviteCodeResultDto> redeemed = await RedeemForOwnerAsync(
            Guid.Parse("0192a000-0000-7000-8000-0000000000ff"),
            "NOCHAN"
        );

        redeemed.IsFailure.Should().BeTrue();
        redeemed.ErrorCode.Should().Be("NOT_FOUND");
        (await StoredGrantsAsync()).Should().BeEmpty();
        (await StoredInviteAsync("NOCHAN")).RedemptionCount.Should().Be(0);
    }

    [Fact]
    public async Task Validate_previews_the_grants_and_rejects_an_unknown_code()
    {
        await AddInviteAsync("PREVIEW", months: 4, maxRedemptions: 3, foundersBadge: true);

        using AppDbContext db = NewDbContext();
        InviteCodeService sut = NewService(db);
        InviteCodeValidationDto valid = (await sut.ValidateAsync("PREVIEW")).Value;

        valid.IsValid.Should().BeTrue();
        valid.GrantsFoundersBadge.Should().BeTrue();
        valid.GrantsTierKey.Should().Be("unlimited");
        valid.GrantsMonths.Should().Be(4);
        valid.RemainingRedemptions.Should().Be(3);
        (await sut.ValidateAsync("NOPE")).ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GrantFoundersBadge_grants_directly_and_is_idempotent()
    {
        using AppDbContext db = NewDbContext();
        InviteCodeService sut = NewService(db);

        Result<FoundersBadgeDto> first = await sut.GrantFoundersBadgeAsync(ChannelId);
        await sut.GrantFoundersBadgeAsync(ChannelId);

        first.Value.IsActive.Should().BeTrue();
        (await db.FoundersBadges.CountAsync(b => b.BroadcasterId == ChannelId)).Should().Be(1);
        _bus.Published.OfType<FoundersBadgeGrantedEvent>().Should().ContainSingle();
        (await sut.GetFoundersBadgeAsync(ChannelId)).Value.Should().NotBeNull();
    }

    [Fact]
    public async Task A_new_sign_up_gets_one_Base_trial_that_ends_after_the_configured_days()
    {
        await SignUpAsync(trialDays: 14);
        await SignUpAsync(trialDays: 14);

        EntitlementGrant trial = (await StoredGrantsAsync()).Should().ContainSingle().Subject;
        trial.BroadcasterId.Should().Be(ChannelId);
        trial.Reason.Should().Be(EntitlementGrant.SignUpTrialReason);
        trial.InviteCodeId.Should().BeNull();
        trial.IssuedByAdminId.Should().BeNull();
        trial.ExpiresAt.Should().Be(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
        (await ResolveAsync()).TierKey.Should().Be(EffectiveTierRule.BaseTierKey);
    }

    [Fact]
    public async Task A_sign_up_gets_no_trial_when_the_trial_is_set_to_zero_days()
    {
        await SignUpAsync(trialDays: 0);

        (await StoredGrantsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_sign_up_with_an_invite_keeps_only_the_invite_gift()
    {
        InviteCode invite = await AddInviteAsync("TRIAL", months: 3);

        await SignUpAsync(trialDays: 14);
        Result<RedeemInviteCodeResultDto> redeemed = await RedeemForOwnerAsync(
            OwnerUserId,
            "TRIAL"
        );

        redeemed.IsSuccess.Should().BeTrue(redeemed.ErrorMessage);
        DateTime now = _time.GetUtcNow().UtcDateTime;
        List<EntitlementGrant> grants = await StoredGrantsAsync();
        grants.Should().HaveCount(2);
        grants
            .Where(g => g.ExpiresAt is null || g.ExpiresAt > now)
            .Should()
            .ContainSingle()
            .Which.InviteCodeId.Should()
            .Be(invite.Id);
        grants
            .Single(g => g.Reason == EntitlementGrant.SignUpTrialReason)
            .ExpiresAt.Should()
            .Be(now);
        (await ResolveAsync()).TierKey.Should().Be("unlimited");
    }
}
