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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;
using NomNomzBot.Infrastructure.Tests.Moderation;
using NomNomzBot.Infrastructure.Trust;

namespace NomNomzBot.Infrastructure.Tests.Trust;

/// <summary>
/// Proves the trust-tuning read/write path (S-OWN23 T3): a channel that never edited anything reads the
/// shipped defaults marked as NOT pinned, an edit persists and reads back, and — the part that protects
/// viewers — a policy that could not produce a sane score is REJECTED rather than stored.
/// </summary>
public sealed class TrustPolicyServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000d1");

    private static (TrustPolicyService Sut, ModerationServiceTestDbContext Db) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        return (new TrustPolicyService(db), db);
    }

    /// <summary>The shipped values, as the editor would post them back unchanged.</summary>
    private static UpdateTrustPolicyRequest DefaultRequest()
    {
        TrustPolicy d = TrustScoreCalculator.DefaultPolicy;
        return new(
            d.RequestCountWeight,
            d.AccountAgeWeight,
            d.ContentAgeWeight,
            d.ContentPopularityWeight,
            d.RequestCountDecay,
            d.AccountAgeDecay,
            d.ContentAgeDecay,
            d.ContentPopularityDecay,
            d.NotFollowingFactor,
            d.ReputationBoostEnabled,
            d.YouTubeQualityPenaltyFactor,
            d.SkipPenalty,
            d.TimeoutPenalty,
            d.BanPenalty,
            d.UntrustedMax,
            d.LowMax,
            d.StandardMax,
            d.HeatHalfLifeHours,
            d.HeatDeltaBan,
            d.HeatDeltaTimeout,
            d.HeatDeltaReportValidated,
            d.HeatDeltaAutoModDenied,
            d.HeatDeltaFilterHit
        );
    }

    [Fact]
    public async Task AnUneditedChannel_ReadsTheShippedDefaults_MarkedNotPinned()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.GetForEditingAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPinned.Should().BeFalse("nothing was ever saved for this channel");
        result.Value.AccountAgeWeight.Should().Be(0.25);
        result.Value.BanPenalty.Should().Be(30.0);
        result.Value.HeatDeltaBan.Should().Be(40m);
    }

    [Fact]
    public async Task AnEdit_Persists_AndReadsBackAsPinned()
    {
        (TrustPolicyService sut, ModerationServiceTestDbContext db) = Build();
        UpdateTrustPolicyRequest request = DefaultRequest() with
        {
            AccountAgeWeight = 0.40,
            ContentAgeWeight = 0.15,
            BanPenalty = 45.0,
            HeatHalfLifeHours = 12.0,
        };

        Result<TrustPolicyDto> saved = await sut.UpdateAsync(Channel, request);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.IsPinned.Should().BeTrue();

        // Round-trips through the database, not just the returned object.
        Result<TrustPolicyDto> reread = await sut.GetForEditingAsync(Channel);
        reread.Value.AccountAgeWeight.Should().Be(0.40);
        reread.Value.BanPenalty.Should().Be(45.0);
        reread.Value.HeatHalfLifeHours.Should().Be(12.0);
        reread.Value.IsPinned.Should().BeTrue();
        (await db.TrustPolicies.CountAsync()).Should().Be(1, "an edit creates exactly one row");
    }

    [Fact]
    public async Task ASecondEdit_UpdatesTheSameRow_RatherThanAddingAnother()
    {
        (TrustPolicyService sut, ModerationServiceTestDbContext db) = Build();

        await sut.UpdateAsync(Channel, DefaultRequest() with { BanPenalty = 45.0 });
        await sut.UpdateAsync(Channel, DefaultRequest() with { BanPenalty = 20.0 });

        (await db.TrustPolicies.CountAsync()).Should().Be(1);
        (await sut.GetForEditingAsync(Channel)).Value.BanPenalty.Should().Be(20.0);
    }

    [Fact]
    public async Task TheScoringPathSeesTheSavedPolicy_NotTheDefaults()
    {
        // GetAsync is what the scorer calls on every action — it must return the channel's own values.
        (TrustPolicyService sut, _) = Build();
        await sut.UpdateAsync(Channel, DefaultRequest() with { HeatDeltaBan = 12m });

        TrustPolicy forScoring = await sut.GetAsync(Channel);

        forScoring.HeatDeltaBan.Should().Be(12m);
    }

    [Fact]
    public async Task WeightsThatDoNotSumToOne_AreRejected_AndNothingIsStored()
    {
        (TrustPolicyService sut, ModerationServiceTestDbContext db) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                AccountAgeWeight = 0.90,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.TrustPolicies.CountAsync())
            .Should()
            .Be(0, "a rejected policy must never reach the database");
    }

    [Fact]
    public async Task TierCeilingsThatDoNotAscend_AreRejected()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                UntrustedMax = 60.0,
                LowMax = 50.0,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task ANegativeViolationPenalty_IsRejected_BecauseItWouldRewardTheViolation()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                BanPenalty = -10.0,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task AZeroHeatHalfLife_IsRejected_BecauseHeatWouldNeverDecay()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                HeatHalfLifeHours = 0.0,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task AZeroGrowthSpeed_IsRejected_BecauseItFreezesThatScorePartAtZero()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                AccountAgeDecay = 0.0,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task OneChannelsTuning_DoesNotLeakIntoAnother()
    {
        (TrustPolicyService sut, _) = Build();
        Guid otherChannel = Guid.Parse("0192d000-0000-7000-8000-0000000000d9");
        await sut.UpdateAsync(Channel, DefaultRequest() with { BanPenalty = 5.0 });

        TrustPolicy other = await sut.GetAsync(otherChannel);

        other.BanPenalty.Should().Be(30.0, "the other channel still tracks the shipped default");
    }
}
