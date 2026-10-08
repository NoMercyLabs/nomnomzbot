// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>Which of the two newcomer limits a message tripped.</summary>
public enum AccountAgeGateKind
{
    /// <summary>The platform account itself is younger than the channel's limit.</summary>
    AccountTooYoung,

    /// <summary>The account follows the channel, but only since less than the channel's limit ago.</summary>
    FollowTooYoung,
}

/// <summary>A tripped newcomer limit, with the explanation a moderator reads.</summary>
/// <param name="Kind">Which limit.</param>
/// <param name="HoldsForReview">True to hold the message for a moderator, false to remove it outright.</param>
/// <param name="Reason">Plain-language reason, naming the limit.</param>
public sealed record AccountAgeGateVerdict(
    AccountAgeGateKind Kind,
    bool HoldsForReview,
    string Reason
);

/// <summary>
/// The newcomer gate: chat from an account, or a follow, younger than the channel's limit is held or
/// removed. Standing wins over age — a Regular and everyone above (SD8 included) is never gated.
///
/// <para>Unknown is not young. A missing account age, or a follow we could not look up, never trips a
/// limit, and a viewer who does not follow is outside the follow limit (followers-only mode is the tool
/// for that).</para>
/// </summary>
public static class AccountAgeGate
{
    /// <summary>Tiers at or above this earned their place in the channel and are never gated.</summary>
    public const SpamTrustTier ExemptFrom = SpamTrustTier.Regular;

    /// <summary>True when this tier is past the gate without being measured.</summary>
    public static bool IsExempt(SpamTrustTier tier) => tier >= ExemptFrom;

    public static AccountAgeGateVerdict? Evaluate(
        SpamDefenseSettings settings,
        AccountFacts facts,
        SpamTrustTier tier
    )
    {
        if (IsExempt(tier))
            return null;

        if (settings.AccountAgeGateDays > 0 && facts.AccountAgeDays < settings.AccountAgeGateDays)
            return new AccountAgeGateVerdict(
                AccountAgeGateKind.AccountTooYoung,
                settings.AccountGateHoldsForReview,
                $"Account is younger than the channel's limit of {settings.AccountAgeGateDays} day(s)."
            );

        if (
            settings.FollowAgeGateDays > 0
            && facts.Follow == FollowState.Following
            && facts.FollowAgeHours < settings.FollowAgeGateDays * 24.0
        )
            return new AccountAgeGateVerdict(
                AccountAgeGateKind.FollowTooYoung,
                settings.AccountGateHoldsForReview,
                $"Follow is younger than the channel's limit of {settings.FollowAgeGateDays} day(s)."
            );

        return null;
    }
}
