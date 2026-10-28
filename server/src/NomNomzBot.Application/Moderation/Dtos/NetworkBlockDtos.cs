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

/// <summary>Whether an admin network block reaches this channel (off until the channel owner says so).</summary>
public sealed record NetworkBlockOptInDto(bool Accepts);
