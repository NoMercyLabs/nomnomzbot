// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Application.Community.Dtos;

/// <summary>
/// Everything the domain model tracks about one person within one channel (owner punch list 2026-09-08 §3,
/// the Community Profile page). Every group here mirrors a row of that punch-list table; a null/empty group
/// means the person genuinely has no data there yet — never a fetch that silently failed.
/// </summary>
public sealed record ViewerProfileSummaryDto(
    ViewerIdentityDto Identity,
    // The J.4/J.5 rollup + trust — the SAME DTOs the Moderation Desk quick-action popup already renders
    // (UserModerationHistorySummaryDto/UserTrustSummaryDto), not a second summary shape. Null when the
    // person has no projected moderation history yet. The full per-action log pages from
    // GET /channels/{channelId}/moderation/history/{userId}, not embedded here.
    UserModerationHistorySummaryDto? ModerationHistory,
    UserTrustSummaryDto? Trust,
    ViewerProfileDto? Activity,
    WatchStreakDto? Streak,
    ViewerEconomyDto Economy,
    ViewerPermitsDto Permits,
    ViewerOverridesDto Overrides,
    ViewerCommandUsageDto CommandUsage,
    ViewerRaidsDto Raids,
    IReadOnlyList<ViewerDatumDto> FreeFormData,
    // Bounded first page (newest first); page further via GET /channels/{channelId}/quotes?userId=.
    IReadOnlyList<QuoteDto> RecentQuotes,
    int TotalQuoteCount
);

/// <summary>Identity & standing (owner punch list §3 row 1).</summary>
public sealed record ViewerIdentityDto(
    Guid UserId,
    string? TwitchUserId,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    string? Pronoun,
    string? AltPronoun,
    IReadOnlyList<LinkedIdentityDto> LinkedIdentities,
    // The ladder-valued chat-badge standing (roles-permissions Plane A — subscriber/VIP/moderator/…).
    string CommunityStanding,
    string? SubTier,
    // The management-role side (Plane B) — broadcaster/moderator/… when this person also manages the channel.
    string? ManagementRole,
    DateTime? MemberSinceUtc,
    DateTime FirstSeenUtc,
    // Twitch account creation date (Helix created_at) — the account age. Null until the profile is first read.
    DateTime? AccountCreatedAtUtc,
    string? Description,
    // "" | "affiliate" | "partner" (Helix broadcaster_type) — stored as given.
    string BroadcasterType,
    // "" | "staff" | "admin" | "global_mod" (Helix type) — stored as given.
    string UserType,
    // The person's own chat color; when they have none, the color the bot assigned them in THIS channel.
    string? ChatColor,
    string? Nickname,
    string? Timezone,
    bool IsLurking,
    // True when a person (not the pronoun API) chose the pronoun.
    bool PronounIsManualOverride,
    // When this person followed THIS channel on Twitch. Null when they do not follow or the platform could not say.
    DateTime? FollowedAtUtc,
    // The bot's own trust tier for this person in this channel (Untrusted … Established), resolved by the
    // spam-defense ladder. Not Twitch's account type. Null when the tier could not be resolved.
    string? TrustTier = null
);

public sealed record LinkedIdentityDto(
    string Provider,
    string ProviderUsername,
    string? ProviderDisplayName,
    string? ProviderAvatarUrl,
    bool IsPrimary,
    DateTime LinkedAtUtc,
    DateTime? LastLoginAtUtc
);

/// <summary>Economy & games (owner punch list §3 row 4).</summary>
public sealed record ViewerEconomyDto(
    CurrencyAccountDto? Wallet,
    int GiveawayEntryCount,
    int GiveawayWinCount,
    bool LeaderboardOptedOut
);

/// <summary>Permits & consent (owner punch list §3 row 5).</summary>
public sealed record ViewerPermitsDto(
    IReadOnlyList<PermitGrantDto> ActivePermits,
    bool? AgeConsentGranted,
    DateTime? AgeConsentConfirmedUtc
);

/// <summary>
/// Custom bot behavior overrides — the person's one shoutout line (also used when raiding them) and TTS
/// voice (owner punch list §3 row 6).
/// </summary>
public sealed record ViewerOverridesDto(string? ShoutoutMessageTemplate, UserTtsVoiceDto? TtsVoice);

/// <summary>Command usage (owner punch list §3 row 7).</summary>
public sealed record ViewerCommandUsageDto(int TotalCommandsUsed, DateTime? LastUsedUtc);

/// <summary>
/// Completed raids between this person's channel and this channel, counted from the journaled EventSub
/// <c>channel.raid</c> rows (the executed signal, not the countdown start). Zero means none were recorded.
/// </summary>
public sealed record ViewerRaidsDto(int TimesRaidedThisChannel, int TimesThisChannelRaidedThem);

/// <summary>One free-form <c>ViewerDatum</c> key/value pair (owner punch list §3 row 8).</summary>
public sealed record ViewerDatumDto(string Key, string Value);
