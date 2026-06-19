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

public sealed record ModerationRuleListItem(
    int Id,
    string Name,
    string Type,
    bool IsEnabled,
    string Action,
    int? DurationSeconds,
    DateTime CreatedAt
);

public sealed record ModerationRuleDetail(
    int Id,
    string Name,
    string Type,
    bool IsEnabled,
    string Action,
    int? DurationSeconds,
    string? Reason,
    Dictionary<string, object?> Settings,
    List<string> ExemptRoles,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record ModerationActionLog(
    string Id,
    string Action,
    string ModeratorId,
    string ModeratorUsername,
    string? TargetUserId,
    string? TargetUsername,
    string? Reason,
    int? DurationSeconds,
    DateTime Timestamp
);

public sealed record CreateModerationRuleRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Action { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? ExemptRoles { get; init; }
}

public sealed record UpdateModerationRuleRequest
{
    public string? Name { get; init; }
    public string? Action { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? ExemptRoles { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed record PerformModerationActionRequest
{
    public required string Action { get; init; }
    public required string TargetUserId { get; init; }
    public string? Reason { get; init; }
    public int? DurationSeconds { get; init; }
}

public sealed record ModerationActionResult(bool Success, string? Message);

// ─── AutoMod Config ──────────────────────────────────────────────────────────

public sealed record AutomodLinkFilterDto(bool Enabled, List<string> Whitelist);

public sealed record AutomodCapsFilterDto(bool Enabled, int Threshold);

public sealed record AutomodBannedPhrasesDto(bool Enabled, List<string> Phrases);

public sealed record AutomodEmoteSpamDto(bool Enabled, int MaxEmotes);

public sealed record AutomodConfigDto(
    AutomodLinkFilterDto LinkFilter,
    AutomodCapsFilterDto CapsFilter,
    AutomodBannedPhrasesDto BannedPhrases,
    AutomodEmoteSpamDto EmoteSpam
);

// ─── Bans ────────────────────────────────────────────────────────────────────

public sealed record BannedUserDto(
    string UserId,
    string Username,
    string? Reason,
    string BannedBy,
    DateTime BannedAt
);

// ─── Mod Log ─────────────────────────────────────────────────────────────────

public sealed record ModLogEntryDto(
    string Id,
    string Action,
    string Moderator,
    string? Target,
    string? Reason,
    DateTime Timestamp,
    int? Duration
);
