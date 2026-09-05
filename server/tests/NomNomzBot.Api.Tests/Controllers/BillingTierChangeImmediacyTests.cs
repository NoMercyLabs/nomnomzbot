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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Billing;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-BUDGETS-b4: proves a channel's write-path ceiling and its reported usage follow a tier CHANGE immediately —
/// no re-login, no stale cache, no baked-in JWT claim. <see cref="BillingTierService"/> and
/// <see cref="ResourceQuotaService"/> resolve the tier fresh from the DB on every call (constructor-injected
/// <c>IApplicationDbContext</c>, no memoization field, no static/singleton cache) — this suite exercises that
/// through the real classes, not a mock, so a regression that introduces caching would break these tests for the
/// right reason. Exercises <c>sound_clip_storage_bytes</c> — a COST_DRIVING, tier-scaled resource (unlike a
/// NEAR_FREE key such as <c>custom_commands</c>, whose ceiling is the uniform safety baseline and never moves
/// with tier by design).
/// </summary>
public sealed class BillingTierChangeImmediacyTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    /// <summary>A metering fake that reports no accumulated period-counter usage — irrelevant here, since
    /// <c>sound_clip_storage_bytes</c> is a live gauge read straight from <c>SoundClips</c>, not a metered
    /// period counter.</summary>
    private sealed class NoUsageMeteringService : IUsageMeteringService
    {
        public Task<Result> RecordAsync(
            Guid broadcasterId,
            string metricKey,
            long quantity,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<QuotaCheckDto>> CheckAsync(
            Guid broadcasterId,
            string metricKey,
            long requestedQuantity,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<UsageMetricDto>>> GetCurrentUsageAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<UsageMetricDto>>([]));

        public Task<Result<int>> ReportUnbilledUsageToStripeAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static async Task<(
        BillingTierChangeTestDbContext Db,
        BillingTier LowTier,
        BillingTier HighTier,
        Subscription Subscription
    )> SeedSaasChannelOnLowTierAsync()
    {
        BillingTierChangeTestDbContext db = BillingTierChangeTestDbContext.New();

        Channel channel = new()
        {
            Id = Broadcaster,
            OwnerUserId = Guid.CreateVersion7(),
            Name = "seedchannel",
            NameNormalized = "seedchannel",
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
        };
        db.Channels.Add(channel);

        BillingTier lowTier = new()
        {
            Key = "base",
            DisplayName = "Base",
            Currency = "usd",
            IsPublic = true,
            SortOrder = 0,
        };
        BillingTier highTier = new()
        {
            Key = "pro",
            DisplayName = "Pro",
            Currency = "usd",
            IsPublic = true,
            SortOrder = 1,
        };
        db.BillingTiers.AddRange(lowTier, highTier);

        // sound_clip_storage_bytes: low tier allows 1,000 bytes; the raised tier allows 1,000,000.
        db.TierLimits.AddRange(
            new TierLimit
            {
                TierId = lowTier.Id,
                LimitKey = "sound_clip_storage_bytes",
                LimitValue = 1_000,
            },
            new TierLimit
            {
                TierId = highTier.Id,
                LimitKey = "sound_clip_storage_bytes",
                LimitValue = 1_000_000,
            }
        );

        Subscription subscription = new()
        {
            BroadcasterId = Broadcaster,
            TierId = lowTier.Id,
            Status = SubscriptionStatus.Active,
        };
        db.Subscriptions.Add(subscription);

        // One existing 800-byte sound clip — already close to the low tier's 1,000-byte ceiling.
        db.SoundClips.Add(
            new SoundClip
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Broadcaster,
                Name = "clip",
                DisplayName = "Clip",
                StorageKey = "clips/clip.wav",
                MimeType = "audio/wav",
                SizeBytes = 800,
            }
        );

        await db.SaveChangesAsync();
        return (db, lowTier, highTier, subscription);
    }

    [Fact]
    public async Task Write_path_refuses_at_the_old_ceiling_then_allows_immediately_after_a_tier_raise()
    {
        (
            BillingTierChangeTestDbContext db,
            BillingTier _,
            BillingTier highTier,
            Subscription subscription
        ) = await SeedSaasChannelOnLowTierAsync();

        BillingTierService tiers = new(db);
        ResourceQuotaService quota = new(
            tiers,
            new NoUsageMeteringService(),
            db,
            TimeProvider.System
        );

        // At the low tier (1,000-byte ceiling), uploading a 900-byte clip on top of the existing 800 bytes
        // (resulting total 1,700) is refused.
        Result<QuotaCheckDto> before = await quota.CheckAsync(
            Broadcaster,
            "sound_clip_storage_bytes",
            resultingCount: 1_700,
            CancellationToken.None
        );
        before.IsSuccess.Should().BeTrue();
        before.Value.Allowed.Should().BeFalse("the channel is already at the low tier's ceiling");
        before.Value.Limit.Should().Be(1_000);

        // The tier changes — simulating ChangeTierAsync's effect on the Subscription row. No cache to bust: no
        // logout, no re-login, no new JWT, no service re-construction. The very next call must see it.
        subscription.TierId = highTier.Id;
        await db.SaveChangesAsync();

        Result<QuotaCheckDto> after = await quota.CheckAsync(
            Broadcaster,
            "sound_clip_storage_bytes",
            resultingCount: 1_700,
            CancellationToken.None
        );
        after.IsSuccess.Should().BeTrue();
        after
            .Value.Allowed.Should()
            .BeTrue("the raised tier's ceiling (1,000,000) now covers the resulting total");
        after.Value.Limit.Should().Be(1_000_000);
    }

    [Fact]
    public async Task Usage_report_reflects_the_new_limit_immediately_after_a_tier_raise()
    {
        (
            BillingTierChangeTestDbContext db,
            BillingTier _,
            BillingTier highTier,
            Subscription subscription
        ) = await SeedSaasChannelOnLowTierAsync();

        BillingTierService tiers = new(db);
        ResourceQuotaService quota = new(
            tiers,
            new NoUsageMeteringService(),
            db,
            TimeProvider.System
        );
        BillingController controller = new(
            subscriptions: null!,
            tiers: tiers,
            metering: new NoUsageMeteringService(),
            quota: quota,
            invites: null!,
            configuration: null!
        );

        IActionResult beforeResult = await controller.GetLimits(
            Broadcaster.ToString(),
            CancellationToken.None
        );
        ResourceUsageDto beforeRow = ExtractSoundClipStorageRow(beforeResult);
        beforeRow.CurrentCount.Should().Be(800);
        beforeRow.Limit.Should().Be(1_000);

        // Raise the tier — same DB write ChangeTierAsync performs, nothing else touched.
        subscription.TierId = highTier.Id;
        await db.SaveChangesAsync();

        // GET .../billing/limits is the SAME endpoint the dashboard polls; no re-login, no reconnect.
        IActionResult afterResult = await controller.GetLimits(
            Broadcaster.ToString(),
            CancellationToken.None
        );
        ResourceUsageDto afterRow = ExtractSoundClipStorageRow(afterResult);
        afterRow.CurrentCount.Should().Be(800, "the underlying stored bytes did not change");
        afterRow.Limit.Should().Be(1_000_000, "the report must show the NEW ceiling at once");
    }

    private static ResourceUsageDto ExtractSoundClipStorageRow(IActionResult result)
    {
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ResourceUsageDto>> body =
            (StatusResponseDto<IReadOnlyList<ResourceUsageDto>>)ok.Value!;
        return body.Data!.Single(r => r.LimitKey == "sound_clip_storage_bytes");
    }
}
