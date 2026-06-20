// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Moderation" category wire models (the /moderation/* endpoints: bans, unban requests, blocked
// terms, chat deletion, shield mode, warnings, suspicious users, and AutoMod). These records deserialize
// straight from Twitch's snake_case JSON via the transport's naming policy — no per-property annotations.
// Twitch ids stay strings (target / moderator / broadcaster ids are other actors' ids); timestamps are
// DateTimeOffset; the owning tenant is always passed in as a Guid method argument, never modelled here.

/// <summary>The result of a Ban User / Timeout User call — who was banned, by which moderator, and the timeout end (null for a permanent ban).</summary>
public sealed record TwitchBanResult(
    string BroadcasterId,
    string ModeratorId,
    string UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndTime
);

/// <summary>One banned or timed-out user (Get Banned Users) — the offender, the moderator who acted, the reason, and when the timeout expires (null for a permanent ban).</summary>
public sealed record TwitchBannedUser(
    string UserId,
    string UserLogin,
    string UserName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    string Reason,
    string ModeratorId,
    string ModeratorLogin,
    string ModeratorName
);

/// <summary>One unban request on the channel (Get Unban Requests / Resolve Unban Request) — the requesting user, the request text, its status, and the moderator's resolution.</summary>
public sealed record TwitchUnbanRequest(
    string Id,
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string ModeratorId,
    string ModeratorLogin,
    string ModeratorName,
    string UserId,
    string UserLogin,
    string UserName,
    string Text,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string ResolutionText
);

/// <summary>One blocked term in the channel's word/phrase block list (Get / Add Blocked Term) — its id, the literal text, and its create/update/expiry timestamps (expiry null when permanent).</summary>
public sealed record TwitchBlockedTerm(
    string BroadcasterId,
    string ModeratorId,
    string Id,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt
);

/// <summary>The channel's Shield Mode activation status (Get / Update Shield Mode Status) — whether it is active, the moderator who last toggled it, and when it was last activated.</summary>
public sealed record TwitchShieldModeStatus(
    bool IsActive,
    string ModeratorId,
    string ModeratorLogin,
    string ModeratorName,
    DateTimeOffset? LastActivatedAt
);

/// <summary>The result of a Warn Chat User call — the warned user, the channel, the acting moderator, and the warning reason.</summary>
public sealed record TwitchWarningResult(
    string BroadcasterId,
    string UserId,
    string ModeratorId,
    string Reason
);

/// <summary>The result of an Add / Remove Suspicious Status call — the chatter, the channel, the acting moderator, when it changed, the resulting status, and any monitoring types.</summary>
public sealed record TwitchSuspiciousUserStatus(
    string UserId,
    string BroadcasterId,
    string ModeratorId,
    DateTimeOffset UpdatedAt,
    string Status,
    IReadOnlyList<string> Types
);

/// <summary>One Check AutoMod Status result — whether the given message would be permitted into chat without being held for review.</summary>
public sealed record TwitchAutoModStatus(string MsgId, bool IsPermitted);

/// <summary>
/// The channel's AutoMod settings (Get / Update AutoMod Settings) — the overall sensitivity level plus the
/// individual category levels. Twitch's AutoMod is driven either by a single <c>OverallLevel</c> (0–4, where
/// 0 disables AutoMod) or by the nine per-category levels; supplying <c>OverallLevel</c> overrides the rest.
/// </summary>
public sealed record TwitchAutoModSettings(
    string BroadcasterId,
    string ModeratorId,
    int? OverallLevel,
    int Disability,
    int Aggression,
    int SexualitySexOrGender,
    int Misogyny,
    int Bullying,
    int Swearing,
    int RaceEthnicityOrReligion,
    int SexBasedTerms
);

/// <summary>
/// Add / Remove Suspicious Status request status — the two states a chatter can be flagged with
/// (<c>ACTIVE_MONITORING</c> or <c>RESTRICTED</c>); removal clears the flag back to no treatment.
/// </summary>
public sealed record SuspiciousUserStatusRequest(string UserId, string Status);

/// <summary>
/// Update AutoMod Settings request body. All fields optional — only the ones set are sent (the transport
/// omits nulls). Supply either <c>OverallLevel</c> to drive every category from one dial, or the individual
/// category levels; mixing the two is rejected by Twitch. The broadcaster is the Guid method argument.
/// </summary>
public sealed record UpdateAutoModSettingsRequest(
    int? OverallLevel = null,
    int? Aggression = null,
    int? Bullying = null,
    int? Disability = null,
    int? Misogyny = null,
    int? RaceEthnicityOrReligion = null,
    int? SexBasedTerms = null,
    int? SexualitySexOrGender = null,
    int? Swearing = null
);
