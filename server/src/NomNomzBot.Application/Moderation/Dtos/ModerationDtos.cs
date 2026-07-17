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

/// <summary>
/// A per-user moderation summary for the mod panel — the bot's RECORDED actions against one viewer in a channel
/// (bans / timeouts / warns / unbans, counts + the most recent). This is the bot's own action history, not Twitch's
/// complete record: actions taken outside the bot (Twitch's UI, other tools) are not counted here.
/// </summary>
public sealed record UserModerationContextDto(
    string UserId,
    string? Username,
    int BanCount,
    int TimeoutCount,
    int WarnCount,
    int UnbanCount,
    string? LastActionType,
    DateTime? LastActionAt,
    IReadOnlyList<ModerationActionLog> RecentActions,
    // The viewer's bot-side standings (J.12) — one entry per platform identity, empty when normal.
    IReadOnlyList<ModerationStandingDto> Standings
);

/// <summary>One platform identity's bot-side standing (J.12): muted | shadowbanned | blacklisted.</summary>
public sealed record ModerationStandingDto(
    string UserId,
    string Provider,
    string Standing,
    string? Reason,
    DateTime UpdatedAt
);

/// <summary>Request body for setting a viewer's bot-side standing.</summary>
public sealed record SetModerationStandingRequest(string Provider, string Standing, string? Reason);

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

// ─── User notes (mod panel) ────────────────────────────────────────────────────

/// <summary>One moderator note pinned to a viewer — free-text context the mod team shares about them.</summary>
public sealed record UserNoteDto(
    int Id,
    string SubjectUserId,
    string Content,
    bool Pinned,
    string? AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Add a note about a viewer.</summary>
public sealed record CreateUserNoteRequest
{
    public required string Content { get; init; }
    public bool Pinned { get; init; }
}

/// <summary>Edit a note's text and/or pinned state (only the supplied fields change).</summary>
public sealed record UpdateUserNoteRequest
{
    public string? Content { get; init; }
    public bool? Pinned { get; init; }
}

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

// ─── Unban requests ────────────────────────────────────────────────────────────

/// <summary>One unban request in the channel's queue, read live from Twitch — the requesting viewer, their
/// appeal text, its status, and (once resolved) the moderator and resolution note.</summary>
public sealed record UnbanRequestDto(
    string Id,
    string UserId,
    string UserLogin,
    string UserName,
    string Text,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedBy,
    string? ResolutionText
);

/// <summary>Approve or deny a pending unban request, with an optional note the viewer sees.</summary>
public sealed record ResolveUnbanRequestRequest
{
    public required bool Approve { get; init; }
    public string? Note { get; init; }
}

// ─── Per-user enforcement (warn / suspicious) ──────────────────────────────────

/// <summary>Issue a warning to a chatter — they must acknowledge it before chatting again (Twitch Warn Chat User).</summary>
public sealed record WarnUserRequest
{
    public required string TargetUserId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Flag a chatter as suspicious — <c>active_monitoring</c> (watch) or <c>restricted</c> (their messages are held).</summary>
public sealed record SetSuspiciousStatusRequest
{
    public required string TargetUserId { get; init; }
    public required string Status { get; init; }
}

/// <summary>A chatter's suspicious-user status after an add/clear, read back from Twitch — the resulting status and any monitoring types.</summary>
public sealed record SuspiciousStatusDto(
    string UserId,
    string Status,
    IReadOnlyList<string> Types,
    DateTime UpdatedAt
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
