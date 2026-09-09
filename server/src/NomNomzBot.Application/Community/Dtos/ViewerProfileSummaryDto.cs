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
    DateTime FirstSeenUtc
);

public sealed record LinkedIdentityDto(
    string Provider,
    string ProviderUsername,
    string? ProviderDisplayName,
    string? ProviderAvatarUrl,
    bool IsPrimary
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

/// <summary>Custom bot behavior overrides — shoutout, raid, TTS voice (owner punch list §3 row 6).</summary>
public sealed record ViewerOverridesDto(
    string? ShoutoutMessageTemplate,
    string? RaidMessageTemplate,
    UserTtsVoiceDto? TtsVoice
);

/// <summary>Command usage (owner punch list §3 row 7).</summary>
public sealed record ViewerCommandUsageDto(int TotalCommandsUsed, DateTime? LastUsedUtc);

/// <summary>One free-form <c>ViewerDatum</c> key/value pair (owner punch list §3 row 8).</summary>
public sealed record ViewerDatumDto(string Key, string Value);
