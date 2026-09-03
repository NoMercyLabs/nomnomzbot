// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Detect and record, act on nothing. Ships ON — a channel that has never been configured must not
    /// be able to action anybody.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// When the channel first enabled the stack. The §6.2 seven-day observation period is measured from
    /// here, so "how long have I been watching?" has an answer the dashboard can show.
    /// </summary>
    public DateTime? EnforcementEligibleAt { get; set; }

    // ─── Trust ────────────────────────────────────────────────────────────────

    public double SemiTrustedWatchHoursHere { get; set; } = 10;
    public double SemiTrustedWatchHoursInstance { get; set; } = 25;

    // ─── Content ──────────────────────────────────────────────────────────────

    public double NearDuplicateSimilarity { get; set; } = 0.6;
    public int MinimumSkeletonLength { get; set; } = 8;
    public bool NonLatinScriptGate { get; set; }

    // ─── Campaign ─────────────────────────────────────────────────────────────

    public double QualifyNoStandingShare { get; set; } = 0.80;
    public double DequalifyNoStandingShare { get; set; } = 0.65;
    public int MinimumCohortSize { get; set; } = 5;
    public int WindowSeconds { get; set; } = 600;
    public int MaxWindowSeconds { get; set; } = 1800;
    public int ActionDelaySeconds { get; set; } = 8;
    public bool AutoReverseOnDequalify { get; set; } = true;

    // ─── Bursts ───────────────────────────────────────────────────────────────

    public double FollowSpikeFactor { get; set; } = 5;
    public double JoinBurstFactor { get; set; } = 4;

    // ─── Lockdown ─────────────────────────────────────────────────────────────

    public int LockdownMinutes { get; set; } = 15;
    public bool LockdownAutoExtend { get; set; } = true;
    public int LockdownMaxMinutes { get; set; } = 60;

    // ─── Network ──────────────────────────────────────────────────────────────

    public bool NetworkSubscribe { get; set; } = true;
    public bool NetworkContribute { get; set; }
    public int RequiredCorroborations { get; set; } = 3;

    /// <summary>Project the row into the record the engine reads.</summary>
    public SpamDefenseSettings ToSettings() =>
        new()
        {
            IsEnabled = IsEnabled,
            DryRun = DryRun,
            SemiTrustedWatchHoursHere = SemiTrustedWatchHoursHere,
            SemiTrustedWatchHoursInstance = SemiTrustedWatchHoursInstance,
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
