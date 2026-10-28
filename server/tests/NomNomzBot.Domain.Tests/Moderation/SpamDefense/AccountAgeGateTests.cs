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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// The newcomer gate: chat from an account (or a follow) younger than the channel's limit is held, and
/// anyone with earned standing is never touched.
/// </summary>
public class AccountAgeGateTests
{
    private static SpamDefenseSettings Gate(
        int accountDays = 0,
        int followDays = 0,
        bool hold = true
    ) =>
        new()
        {
            AccountAgeGateDays = accountDays,
            FollowAgeGateDays = followDays,
            AccountGateHoldsForReview = hold,
        };

    [Fact]
    public void AOneDayOldAccount_UnderAThreeDayLimit_IsHeldForReview_WithTheLimitInTheReason()
    {
        AccountAgeGateVerdict? verdict = AccountAgeGate.Evaluate(
            Gate(accountDays: 3),
            new AccountFacts { AccountAgeDays = 1 },
            SpamTrustTier.Untrusted
        );

        verdict.Should().NotBeNull();
        verdict.Kind.Should().Be(AccountAgeGateKind.AccountTooYoung);
        verdict.HoldsForReview.Should().BeTrue();
        verdict.Reason.Should().Contain("3 day");
    }

    [Fact]
    public void WithHoldingOff_TheVerdictRemovesInsteadOfHolding()
    {
        AccountAgeGateVerdict? verdict = AccountAgeGate.Evaluate(
            Gate(accountDays: 3, hold: false),
            new AccountFacts { AccountAgeDays = 1 },
            SpamTrustTier.Untrusted
        );

        verdict!.HoldsForReview.Should().BeFalse();
    }

    [Theory]
    [InlineData(SpamTrustTier.Regular)]
    [InlineData(SpamTrustTier.SemiTrusted)]
    [InlineData(SpamTrustTier.Trusted)]
    [InlineData(SpamTrustTier.Established)]
    public void ARegularOrAbove_WithTheSameYoungAccount_IsNeverGated(SpamTrustTier tier)
    {
        AccountAgeGate
            .Evaluate(
                Gate(accountDays: 3, followDays: 3),
                new AccountFacts
                {
                    AccountAgeDays = 1,
                    Follow = FollowState.Following,
                    FollowAgeHours = 2,
                },
                tier
            )
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData(SpamTrustTier.Untrusted)]
    [InlineData(SpamTrustTier.Newcomer)]
    [InlineData(SpamTrustTier.Known)]
    public void BelowRegular_TheGateApplies(SpamTrustTier tier)
    {
        AccountAgeGate
            .Evaluate(Gate(accountDays: 3), new AccountFacts { AccountAgeDays = 1 }, tier)
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void AZeroLimit_IsOff_ForBothGates()
    {
        AccountAgeGate
            .Evaluate(
                Gate(),
                new AccountFacts
                {
                    AccountAgeDays = 0.1,
                    Follow = FollowState.Following,
                    FollowAgeHours = 0.1,
                },
                SpamTrustTier.Untrusted
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void AnUnknownAccountAge_IsNeverGated_BecauseUnknownIsNotYoung()
    {
        AccountAgeGate
            .Evaluate(Gate(accountDays: 30), new AccountFacts(), SpamTrustTier.Untrusted)
            .Should()
            .BeNull();
    }

    [Fact]
    public void AnAccountAtTheLimit_IsNotGated()
    {
        AccountAgeGate
            .Evaluate(
                Gate(accountDays: 3),
                new AccountFacts { AccountAgeDays = 3 },
                SpamTrustTier.Untrusted
            )
            .Should()
            .BeNull();
    }

    [Fact]
    public void AFollowerOfTwoHours_UnderAOneDayFollowLimit_IsHeld_EvenWithAnOldAccount()
    {
        AccountAgeGateVerdict? verdict = AccountAgeGate.Evaluate(
            Gate(followDays: 1),
            new AccountFacts
            {
                AccountAgeDays = 900,
                Follow = FollowState.Following,
                FollowAgeHours = 2,
            },
            SpamTrustTier.Newcomer
        );

        verdict.Should().NotBeNull();
        verdict.Kind.Should().Be(AccountAgeGateKind.FollowTooYoung);
    }

    [Theory]
    [InlineData(FollowState.NotFollowing)]
    [InlineData(FollowState.Unknown)]
    public void SomeoneWhoDoesNotFollowOrIsUnknown_IsNotHeldByTheFollowGate(FollowState state)
    {
        AccountAgeGate
            .Evaluate(
                Gate(followDays: 1),
                new AccountFacts { AccountAgeDays = 900, Follow = state },
                SpamTrustTier.Untrusted
            )
            .Should()
            .BeNull();
    }
}
