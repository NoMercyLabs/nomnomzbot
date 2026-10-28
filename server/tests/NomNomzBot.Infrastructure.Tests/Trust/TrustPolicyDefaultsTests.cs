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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;
using NomNomzBot.Infrastructure.Tests.Moderation;
using NomNomzBot.Infrastructure.Trust;

namespace NomNomzBot.Infrastructure.Tests.Trust;

/// <summary>
/// One default per trust setting: <see cref="TrustPolicyDefaults"/> is read by the entity, the EF column
/// defaults, the calculator fallback, the API defaults and the validator. A missing row resolves to those
/// defaults, and every refusal names a translatable key and the field that caused it.
/// </summary>
public sealed class TrustPolicyDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000d2");

    private static (TrustPolicyService Sut, ModerationServiceTestDbContext Db) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        return (new TrustPolicyService(db), db);
    }

    private static UpdateTrustPolicyRequest DefaultRequest()
    {
        TrustPolicyDto d = TrustPolicyService.DefaultsDto();
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
            d.AffiliateBoost,
            d.PartnerBoost,
            d.VerifiedYouTubeBoost,
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
    public void TheDefaultsDto_CarriesEveryDefaultConstant_AndIsNotPinned()
    {
        TrustPolicyDto d = TrustPolicyService.DefaultsDto();

        d.IsPinned.Should().BeFalse();
        d.RequestCountWeight.Should().Be(TrustPolicyDefaults.RequestCountWeight);
        d.ContentAgeDecay.Should().Be(TrustPolicyDefaults.ContentAgeDecay);
        d.ContentPopularityDecay.Should().Be(TrustPolicyDefaults.ContentPopularityDecay);
        d.NotFollowingFactor.Should().Be(TrustPolicyDefaults.NotFollowingFactor);
        d.ReputationBoostEnabled.Should().Be(TrustPolicyDefaults.ReputationBoostEnabled);
        d.PartnerBoost.Should().Be(TrustPolicyDefaults.PartnerBoost);
        d.BanPenalty.Should().Be(TrustPolicyDefaults.BanPenalty);
        d.StandardMax.Should().Be(TrustPolicyDefaults.StandardMax);
        d.HeatHalfLifeHours.Should().Be(TrustPolicyDefaults.HeatHalfLifeHours);
        d.HeatDeltaBan.Should().Be(TrustPolicyDefaults.HeatDeltaBan);
        d.HeatDeltaFilterHit.Should().Be(TrustPolicyDefaults.HeatDeltaFilterHit);
    }

    [Fact]
    public async Task AMissingRow_ResolvesToTheDefaultsDto_ForTheEditorRead()
    {
        (TrustPolicyService sut, ModerationServiceTestDbContext db) = Build();

        Result<TrustPolicyDto> result = await sut.GetForEditingAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TrustPolicyService.DefaultsDto());
        (await db.TrustPolicies.CountAsync()).Should().Be(0, "a read never creates a row");
    }

    [Fact]
    public void TheEntityAndTheDefaultsDto_AgreeOnEveryField()
    {
        // A field added to the entity but missed in the defaults mapping fails here, not in production.
        TrustPolicy fresh = new();
        TrustPolicyDto d = TrustPolicyService.DefaultsDto();

        UpdateTrustPolicyRequest asRequest = DefaultRequest();
        foreach (
            System.Reflection.PropertyInfo requestProperty in typeof(UpdateTrustPolicyRequest).GetProperties()
        )
        {
            object? fromEntity = typeof(TrustPolicy)
                .GetProperty(requestProperty.Name)!
                .GetValue(fresh);
            requestProperty.GetValue(asRequest).Should().Be(fromEntity, requestProperty.Name);
        }

        d.IsPinned.Should().BeFalse();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task ANonFiniteNumber_IsRefused_WithTheNotFiniteKeyAndTheField(double bad)
    {
        // NaN slips through every `<` / `>` comparison, so the weights-sum check alone let it be stored.
        (TrustPolicyService sut, ModerationServiceTestDbContext db) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                BanPenalty = bad,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result
            .ErrorData.Should()
            .Be(new TrustPolicyViolation(TrustPolicyViolation.NotFinite, "banPenalty"));
        (await db.TrustPolicies.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WeightsNotSummingToOne_NameTheKey_AndCarryNoStoredRow()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                AccountAgeWeight = 0.90,
            }
        );

        result
            .ErrorData.Should()
            .Be(
                new TrustPolicyViolation(
                    TrustPolicyViolation.WeightsMustSumToOne,
                    "requestCountWeight"
                )
            );
    }

    [Fact]
    public async Task ADecayAboveTheCeiling_IsRefused()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                AccountAgeDecay = TrustPolicyDefaults.MaxDecay + 1,
            }
        );

        result
            .ErrorData.Should()
            .Be(new TrustPolicyViolation(TrustPolicyViolation.DecayOutOfRange, "accountAgeDecay"));
    }

    [Fact]
    public async Task APenaltyAboveTheScoreRange_IsRefused()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                BanPenalty = TrustPolicyDefaults.MaxScore + 1,
            }
        );

        result
            .ErrorData.Should()
            .Be(new TrustPolicyViolation(TrustPolicyViolation.PenaltyOutOfRange, "banPenalty"));
    }

    [Fact]
    public async Task AHeatHalfLifeAboveTheCeiling_IsRefused()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                HeatHalfLifeHours = TrustPolicyDefaults.MaxHeatHalfLifeHours + 1,
            }
        );

        result
            .ErrorData.Should()
            .Be(
                new TrustPolicyViolation(
                    TrustPolicyViolation.HeatHalfLifeOutOfRange,
                    "heatHalfLifeHours"
                )
            );
    }

    [Fact]
    public async Task AHeatDeltaAboveTheCeiling_IsRefused()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> result = await sut.UpdateAsync(
            Channel,
            DefaultRequest() with
            {
                HeatDeltaBan = TrustPolicyDefaults.MaxHeatDelta + 1,
            }
        );

        result
            .ErrorData.Should()
            .Be(new TrustPolicyViolation(TrustPolicyViolation.HeatDeltaOutOfRange, "heatDeltaBan"));
    }

    [Fact]
    public async Task TierCeilingsThatDoNotAscend_NameTheKey()
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

        result
            .ErrorData.Should()
            .Be(new TrustPolicyViolation(TrustPolicyViolation.TiersMustAscend, "lowMax"));
    }

    [Fact]
    public async Task ADefaultsRoundTrip_IsAccepted_AndReadsBackPinned()
    {
        (TrustPolicyService sut, _) = Build();

        Result<TrustPolicyDto> saved = await sut.UpdateAsync(Channel, DefaultRequest());

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.Should().Be(TrustPolicyService.DefaultsDto() with { IsPinned = true });
    }
}
