// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One tenant a prospective network block would touch (S-ADMIN-8b) — real, freshly-computed presence,
/// never a cached or heuristic guess.
/// </summary>
public sealed record NetworkBlockAffectedTenantDto(Guid BroadcasterId, string ChannelName);

/// <summary>
/// The counted blast radius of a network-wide block, computed fresh — required reading BEFORE an operator
/// can commit to applying one. <see cref="TenantCount"/> is echoed back on apply
/// (<see cref="ApplyNetworkBlockRequest.ConfirmedTenantCount"/>) and re-verified fresh; a stale count fails
/// closed rather than acting on numbers the operator never actually saw.
/// </summary>
public sealed record NetworkBlockPreviewDto(
    Guid TargetUserId,
    string TargetTwitchUserId,
    string? TargetDisplayName,
    int TenantCount,
    IReadOnlyList<NetworkBlockAffectedTenantDto> Tenants
);

/// <summary>Apply a network-wide block — the confirmed count must match a freshly recomputed one.</summary>
public sealed record ApplyNetworkBlockRequest(
    string TargetTwitchUserId,
    string? Reason,
    string Justification,
    int ConfirmedTenantCount
);

/// <summary>
/// One network-wide block (S-ADMIN-8b): who applied it, when, why, the blast radius it actually touched,
/// and — once a lift has been attempted — who lifted it, when, why, and whether the lift was actually
/// clean (<see cref="LiftedAt"/> is null on a partial outcome; <see cref="LiftFailedChannelIds"/> then
/// names exactly what is still actioned).
/// </summary>
public sealed record NetworkBlockDto(
    Guid Id,
    Guid TargetUserId,
    string TargetTwitchUserId,
    string? TargetDisplayName,
    string? Reason,
    string Justification,
    Guid AppliedByPrincipalId,
    DateTime AppliedAt,
    int TenantCount,
    int ChannelCount,
    string Status,
    Guid? LiftedByPrincipalId,
    string? LiftJustification,
    DateTime? LiftAttemptedAt,
    DateTime? LiftedAt,
    int RestoredChannelCount,
    IReadOnlyList<string> LiftFailedChannelIds
);
