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

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// The one place each spam-defense setting default is written. A channel with no saved policy row resolves
/// to these values; the entity, the settings record and the API all read them.
/// </summary>
public static class SpamDefenseDefaults
{
    // Master
    public const bool IsEnabled = true;

    /// <summary>Ships ON: a channel that has never been configured must not be able to action anybody.</summary>
    public const bool DryRun = true;

    // Trust
    public const double SemiTrustedWatchHoursHere = 10;
    public const double SemiTrustedWatchHoursInstance = 25;
    public const double EstablishedDays = 90;
    public const int EstablishedMessages = 300;
    public const int EstablishedDistinctActiveDays = 30;
    public const double EstablishedStrikeFreeDays = 180;

    // Newcomers
    public const int AccountAgeGateDays = 0;
    public const int FollowAgeGateDays = 0;
    public const bool AccountGateHoldsForReview = true;

    // Content
    public const double NearDuplicateSimilarity = 0.6;
    public const int MinimumSkeletonLength = 8;
    public const bool NonLatinScriptGate = false;

    // Campaign
    public const double QualifyNoStandingShare = 0.80;
    public const double DequalifyNoStandingShare = 0.65;
    public const int MinimumCohortSize = 5;
    public const int WindowSeconds = 600;
    public const int MaxWindowSeconds = 1800;
    public const int ActionDelaySeconds = 8;
    public const bool AutoReverseOnDequalify = true;

    // Bursts
    public const double FollowSpikeFactor = 5;
    public const double JoinBurstFactor = 4;

    // Lockdown
    public const int LockdownMinutes = 15;
    public const bool LockdownAutoExtend = true;
    public const int LockdownMaxMinutes = 60;

    // Network
    public const bool NetworkSubscribe = true;
    public const bool NetworkContribute = false;
    public const int RequiredCorroborations = 3;
}
