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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One knob as the editor needs it: what it is, where it belongs, and what it is bounded to.
/// The words come from the dashboard's string resources, keyed by these.
/// </summary>
public sealed record SpamSettingDescriptorDto(
    string Key,
    string Group,
    string LabelKey,
    string ExplanationKey,
    string CostKey,
    double? Minimum,
    double? Maximum,
    bool IsToggle
);

/// <summary>
/// The whole configuration surface in one response: the channel's current values, the metadata to
/// render an editor for them, and the guarantees that have no switch.
///
/// <para>They travel together on purpose. A client that fetched values alone would have to hardcode
/// its own idea of the bounds, and the first time a range moved server-side the form would start
/// rejecting saves it had just accepted.</para>
/// </summary>
public sealed record SpamDefensePolicyDto(
    SpamDefenseSettings Settings,
    IReadOnlyList<SpamSettingDescriptorDto> Catalogue,
    IReadOnlyList<SpamInvariantDto> Invariants,
    DateTime? EnforcementEligibleAt,
    bool IsPinned
);

/// <summary>A protection the operator gets for free and cannot turn off.</summary>
public sealed record SpamInvariantDto(string Decision, string GuaranteeKey);

/// <summary>One recorded verdict, for the review queue.</summary>
public sealed record SpamDetectionDto(
    Guid Id,
    string SubjectPlatformUserId,
    string SubjectDisplayName,
    string Provider,
    string MessageId,
    string MessageText,
    string Signals,
    SpamConfidence Confidence,
    SpamTrustTier Tier,
    SpamOutcome Outcome,
    SpamOutcome WouldHaveBeen,
    bool WasDryRun,
    string Reason,
    DateTime? OverturnedAt,
    DateTime DetectedAt
);
