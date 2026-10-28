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

using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One channel's spam-defence configuration (spam-defense.md §6.1), persisted.
///
/// <para>The columns mirror <see cref="SpamDefenseSettings"/> one-for-one, and
/// <see cref="ToSettings"/> / <see cref="ApplySettings"/> are the only conversion — so the record the
/// engine reads and the row an operator edits cannot drift into meaning different things.</para>
///
/// <para>A channel with no row runs on the shipped defaults, which are the safe ones: enabled, and in
/// dry run.</para>
/// </summary>
public class SpamDefensePolicy : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One policy per channel.</summary>
    public Guid BroadcasterId { get; set; }

    // ─── Master ───────────────────────────────────────────────────────────────

    public bool IsEnabled { get; set; } = SpamDefenseDefaults.IsEnabled;

    /// <summary>
    /// Detect and record, act on nothing. Ships ON — a channel that has never been configured must not
    /// be able to action anybody.
    /// </summary>
    public bool DryRun { get; set; } = SpamDefenseDefaults.DryRun;

    /// <summary>
    /// When the channel first enabled the stack. The §6.2 seven-day observation period is measured from
    /// here, so "how long have I been watching?" has an answer the dashboard can show.
    /// </summary>
    public DateTime? EnforcementEligibleAt { get; set; }

    // ─── Trust ────────────────────────────────────────────────────────────────

    public double SemiTrustedWatchHoursHere { get; set; } =
        SpamDefenseDefaults.SemiTrustedWatchHoursHere;
    public double SemiTrustedWatchHoursInstance { get; set; } =
        SpamDefenseDefaults.SemiTrustedWatchHoursInstance;

    /// <summary>Days since first message here that, with the other three, earn Established.</summary>
    public double EstablishedDays { get; set; } = SpamDefenseDefaults.EstablishedDays;
    public int EstablishedMessages { get; set; } = SpamDefenseDefaults.EstablishedMessages;
    public int EstablishedDistinctActiveDays { get; set; } =
        SpamDefenseDefaults.EstablishedDistinctActiveDays;
    public double EstablishedStrikeFreeDays { get; set; } =
        SpamDefenseDefaults.EstablishedStrikeFreeDays;

    // ─── Newcomers ────────────────────────────────────────────────────────────

    public int AccountAgeGateDays { get; set; } = SpamDefenseDefaults.AccountAgeGateDays;
    public int FollowAgeGateDays { get; set; } = SpamDefenseDefaults.FollowAgeGateDays;
    public bool AccountGateHoldsForReview { get; set; } =
        SpamDefenseDefaults.AccountGateHoldsForReview;

    // ─── Content ──────────────────────────────────────────────────────────────

    public double NearDuplicateSimilarity { get; set; } =
        SpamDefenseDefaults.NearDuplicateSimilarity;
    public int MinimumSkeletonLength { get; set; } = SpamDefenseDefaults.MinimumSkeletonLength;
    public bool NonLatinScriptGate { get; set; } = SpamDefenseDefaults.NonLatinScriptGate;

    // ─── Campaign ─────────────────────────────────────────────────────────────

    public double QualifyNoStandingShare { get; set; } = SpamDefenseDefaults.QualifyNoStandingShare;
    public double DequalifyNoStandingShare { get; set; } =
        SpamDefenseDefaults.DequalifyNoStandingShare;
    public int MinimumCohortSize { get; set; } = SpamDefenseDefaults.MinimumCohortSize;
    public int WindowSeconds { get; set; } = SpamDefenseDefaults.WindowSeconds;
    public int MaxWindowSeconds { get; set; } = SpamDefenseDefaults.MaxWindowSeconds;
    public int ActionDelaySeconds { get; set; } = SpamDefenseDefaults.ActionDelaySeconds;
    public bool AutoReverseOnDequalify { get; set; } = SpamDefenseDefaults.AutoReverseOnDequalify;

    // ─── Bursts ───────────────────────────────────────────────────────────────

    public double FollowSpikeFactor { get; set; } = SpamDefenseDefaults.FollowSpikeFactor;
    public double JoinBurstFactor { get; set; } = SpamDefenseDefaults.JoinBurstFactor;

    // ─── Lockdown ─────────────────────────────────────────────────────────────

    public int LockdownMinutes { get; set; } = SpamDefenseDefaults.LockdownMinutes;
    public bool LockdownAutoExtend { get; set; } = SpamDefenseDefaults.LockdownAutoExtend;
    public int LockdownMaxMinutes { get; set; } = SpamDefenseDefaults.LockdownMaxMinutes;

    // ─── Network ──────────────────────────────────────────────────────────────

    public bool NetworkSubscribe { get; set; } = SpamDefenseDefaults.NetworkSubscribe;
    public bool NetworkContribute { get; set; } = SpamDefenseDefaults.NetworkContribute;
    public int RequiredCorroborations { get; set; } = SpamDefenseDefaults.RequiredCorroborations;

    /// <summary>Project the row into the record the engine reads.</summary>
    public SpamDefenseSettings ToSettings() =>
        new()
        {
            IsEnabled = IsEnabled,
            DryRun = DryRun,
            SemiTrustedWatchHoursHere = SemiTrustedWatchHoursHere,
            SemiTrustedWatchHoursInstance = SemiTrustedWatchHoursInstance,
            TrustThresholds = new TrustTierThresholds
            {
                EstablishedDays = EstablishedDays,
                EstablishedMessages = EstablishedMessages,
                EstablishedDistinctActiveDays = EstablishedDistinctActiveDays,
                EstablishedStrikeFreeDays = EstablishedStrikeFreeDays,
            },
            AccountAgeGateDays = AccountAgeGateDays,
            FollowAgeGateDays = FollowAgeGateDays,
            AccountGateHoldsForReview = AccountGateHoldsForReview,
            NearDuplicateSimilarity = NearDuplicateSimilarity,
            MinimumSkeletonLength = MinimumSkeletonLength,
            NonLatinScriptGate = NonLatinScriptGate,
            QualifyNoStandingShare = QualifyNoStandingShare,
            DequalifyNoStandingShare = DequalifyNoStandingShare,
            MinimumCohortSize = MinimumCohortSize,
            WindowSeconds = WindowSeconds,
            MaxWindowSeconds = MaxWindowSeconds,
            ActionDelaySeconds = ActionDelaySeconds,
            AutoReverseOnDequalify = AutoReverseOnDequalify,
            FollowSpikeFactor = FollowSpikeFactor,
            JoinBurstFactor = JoinBurstFactor,
            LockdownMinutes = LockdownMinutes,
            LockdownAutoExtend = LockdownAutoExtend,
            LockdownMaxMinutes = LockdownMaxMinutes,
            NetworkSubscribe = NetworkSubscribe,
            NetworkContribute = NetworkContribute,
            RequiredCorroborations = RequiredCorroborations,
        };

    /// <summary>Write an edited record back onto the row.</summary>
    public void ApplySettings(SpamDefenseSettings settings)
    {
        IsEnabled = settings.IsEnabled;
        DryRun = settings.DryRun;
        SemiTrustedWatchHoursHere = settings.SemiTrustedWatchHoursHere;
        SemiTrustedWatchHoursInstance = settings.SemiTrustedWatchHoursInstance;
        EstablishedDays = settings.TrustThresholds.EstablishedDays;
        EstablishedMessages = settings.TrustThresholds.EstablishedMessages;
        EstablishedDistinctActiveDays = settings.TrustThresholds.EstablishedDistinctActiveDays;
        EstablishedStrikeFreeDays = settings.TrustThresholds.EstablishedStrikeFreeDays;
        AccountAgeGateDays = settings.AccountAgeGateDays;
        FollowAgeGateDays = settings.FollowAgeGateDays;
        AccountGateHoldsForReview = settings.AccountGateHoldsForReview;
        NearDuplicateSimilarity = settings.NearDuplicateSimilarity;
        MinimumSkeletonLength = settings.MinimumSkeletonLength;
        NonLatinScriptGate = settings.NonLatinScriptGate;
        QualifyNoStandingShare = settings.QualifyNoStandingShare;
        DequalifyNoStandingShare = settings.DequalifyNoStandingShare;
        MinimumCohortSize = settings.MinimumCohortSize;
        WindowSeconds = settings.WindowSeconds;
        MaxWindowSeconds = settings.MaxWindowSeconds;
        ActionDelaySeconds = settings.ActionDelaySeconds;
        AutoReverseOnDequalify = settings.AutoReverseOnDequalify;
        FollowSpikeFactor = settings.FollowSpikeFactor;
        JoinBurstFactor = settings.JoinBurstFactor;
        LockdownMinutes = settings.LockdownMinutes;
        LockdownAutoExtend = settings.LockdownAutoExtend;
        LockdownMaxMinutes = settings.LockdownMaxMinutes;
        NetworkSubscribe = settings.NetworkSubscribe;
        NetworkContribute = settings.NetworkContribute;
        RequiredCorroborations = settings.RequiredCorroborations;
    }
}
