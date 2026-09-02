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

/// <summary>One item in the moderation review queue (moderation.md J.1) — a held AutoMod message for this slice.</summary>
public sealed record ModerationQueueItemDto(
    Guid Id,
    string Source,
    string Status,
    string? TargetTwitchUserId,
    string? TargetUsernameSnapshot,
    string? MessageContentSnapshot,
    string? AutoModCategory,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedByName,
    string? ResolutionAction
);

/// <summary>
/// A moderator resolves a held AutoMod message — <c>approve</c> (release it to chat) or <c>deny</c> (drop it).
/// A deny may carry a follow-up moderation action against the sender: <c>timeout</c> (requires
/// <see cref="TimeoutSeconds"/>) or <c>ban</c>, each with an optional <see cref="Reason"/>.
/// </summary>
public sealed record ResolveModerationQueueItemRequest
{
    public required string Action { get; init; }

    /// <summary>Follow-up against the sender after a deny: <c>none</c> (default), <c>timeout</c> or <c>ban</c>.</summary>
    public string? FollowUp { get; init; }

    /// <summary>Timeout length in seconds (1–1209600), required when <see cref="FollowUp"/> is <c>timeout</c>.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Optional reason recorded with a <c>timeout</c>/<c>ban</c> follow-up.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// The service-level outcome of resolving a held message. <see cref="FollowUpError"/> is non-null when the
/// deny itself succeeded but the requested follow-up (timeout/ban) failed — the message is gone from chat,
/// the account action is NOT applied, and the moderator must see both halves of that truth. On the wire the
/// item travels as the envelope's <c>data</c> and the follow-up error as its <c>message</c>.
/// </summary>
public sealed record ResolveModerationQueueItemResultDto(
    ModerationQueueItemDto Item,
    string? FollowUpError
);
