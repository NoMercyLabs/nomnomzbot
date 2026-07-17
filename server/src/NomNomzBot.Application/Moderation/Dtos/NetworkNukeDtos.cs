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

/// <summary>One network-nuke batch (moderation.md §3.4, J.2a). <c>Status</c>: active | partial | reverted.</summary>
public sealed record NetworkNukeBatchDto(
    Guid Id,
    Guid OriginBroadcasterId,
    Guid? InitiatedByUserId,
    string? MatchTerm,
    Guid? TargetUserId,
    string? TargetTwitchUserId,
    int ChannelCount,
    string Status,
    Guid? RevertedByUserId,
    DateTime? RevertedAt,
    DateTime CreatedAt
);

/// <summary>The nuke request — <c>RequireConfirmation</c> MUST be true (the single-confirmation guardrail).</summary>
public sealed record NetworkNukeRequest
{
    public required string TargetTwitchUserId { get; init; }
    public string? Reason { get; init; }
    public string? MatchTerm { get; init; }
    public required bool RequireConfirmation { get; init; }
}
