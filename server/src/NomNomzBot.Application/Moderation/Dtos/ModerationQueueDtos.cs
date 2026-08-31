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

/// <summary>A moderator resolves a held AutoMod message — <c>approve</c> (release it to chat) or <c>deny</c> (drop it).</summary>
public sealed record ResolveModerationQueueItemRequest
{
    public required string Action { get; init; }
}
