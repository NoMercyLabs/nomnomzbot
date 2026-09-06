// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// One person matched by the cross-tenant support search (S-ADMIN-7a). Identity only — the per-tenant facts
/// live on <see cref="SupportPersonViewDto"/>, behind a second audited lookup.
/// </summary>
public sealed record SupportPersonSearchResultDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string Platform,
    string? TwitchUserId,
    bool IsPlatformPrincipal,
    bool IsAnonymized,
    DateTime? LastSeenAt,
    int TenantCount
);

/// <summary>One proven external login this person holds (<c>UserIdentity</c>).</summary>
public sealed record SupportPersonIdentityDto(
    string Provider,
    string ProviderUserId,
    string ProviderUsername,
    bool IsPrimary,
    DateTime LinkedAt,
    DateTime? LastLoginAt
);

/// <summary>One streaming-platform connection on a channel this person OWNS (<c>PlatformConnection</c>).</summary>
public sealed record SupportPersonPlatformConnectionDto(
    Guid BroadcasterId,
    string ChannelName,
    string Provider,
    string ExternalChannelId,
    string DisplayName,
    bool IsPrimary,
    bool IsLive
);

/// <summary>One LIVE platform-IAM role assignment this person holds, platform-wide or narrowed to one tenant.</summary>
public sealed record SupportPersonIamRoleDto(
    Guid PrincipalId,
    string PrincipalName,
    string RoleName,
    Guid? ScopeBroadcasterId,
    string? ScopeChannelName,
    DateTime? ExpiresAt
);

/// <summary>This person's Plane-A community standing in ONE tenant (<c>ChannelCommunityStanding</c>).</summary>
public sealed record SupportPersonCommunityStandingDto(
    Guid BroadcasterId,
    string ChannelName,
    string Standing,
    int LevelValue,
    string Source,
    string? SubTier,
    DateTime? LastSeenAt
);

/// <summary>
/// The bot-side negative moderation standing this person carries in ONE tenant on ONE platform
/// (<c>ChannelModerationStanding</c>). The row is matched through the person's proven platform ids — the
/// entity stores a RAW platform id, never the internal <c>User.Id</c>.
/// </summary>
public sealed record SupportPersonModerationStandingDto(
    Guid BroadcasterId,
    string ChannelName,
    string Provider,
    string Standing,
    string? Reason,
    DateTime CreatedAt
);

/// <summary>This person's moderation rollup in ONE tenant (<c>UserModerationHistory</c>).</summary>
public sealed record SupportPersonModerationHistoryDto(
    Guid BroadcasterId,
    string ChannelName,
    int TimeoutCount,
    int BanCount,
    int WarningCount,
    int MessagesDeletedCount,
    DateTime? LastActionAt,
    string? LastActionType
);

/// <summary>This person's trust + heat projection in ONE tenant (<c>UserTrustScore</c>).</summary>
public sealed record SupportPersonTrustScoreDto(
    Guid BroadcasterId,
    string ChannelName,
    decimal TrustScore,
    decimal HeatScore,
    DateTime? LastHeatEventAt,
    DateTime ComputedAt
);

/// <summary>
/// The effective entitlement of ONE channel this person owns: the tier <c>IBillingTierService</c> actually
/// resolves, plus the LIVE operator comps (<c>EntitlementGrant</c>) behind it. <see cref="Grants"/> is empty
/// when the tenant simply has none — never a placeholder row.
/// </summary>
public sealed record SupportPersonEntitlementDto(
    Guid BroadcasterId,
    string ChannelName,
    string TierKey,
    IReadOnlyList<SupportPersonEntitlementGrantDto> Grants
);

/// <summary>One LIVE operator comp on a channel this person owns (<c>EntitlementGrant</c>).</summary>
public sealed record SupportPersonEntitlementGrantDto(
    Guid GrantId,
    Guid GrantedTierId,
    string Reason,
    DateTime IssuedAt,
    DateTime ExpiresAt
);

/// <summary>
/// One real event recorded about this person in ONE tenant (S-ADMIN-7b), read straight from the append-only
/// <c>EventJournal</c> — the same ledger <c>IEventJournal</c> replay/projection tooling reads, never a second
/// reconstruction. <see cref="BroadcasterId"/> is <c>null</c> for a platform-global event; <see cref="ChannelName"/>
/// mirrors that (also <c>null</c>) rather than inventing a tenant label.
/// </summary>
public sealed record SupportPersonHistoryEntryDto(
    Guid EventId,
    Guid? BroadcasterId,
    string? ChannelName,
    string EventType,
    string Source,
    DateTime OccurredAt
);

/// <summary>
/// ONE cross-tenant view of one person's REAL state (S-ADMIN-7a). Every list is sourced from its own table
/// and every per-tenant fact names the tenant it belongs to. A datum the system does not have for this person
/// is ABSENT — an empty list, never a zeroed placeholder row that would read as real data.
/// </summary>
public sealed record SupportPersonViewDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string Platform,
    string? TwitchUserId,
    bool IsPlatformPrincipal,
    bool IsAnonymized,
    DateTime? LastSeenAt,
    IReadOnlyList<SupportPersonIdentityDto> Identities,
    IReadOnlyList<SupportPersonPlatformConnectionDto> PlatformConnections,
    IReadOnlyList<SupportPersonIamRoleDto> IamRoles,
    IReadOnlyList<SupportPersonCommunityStandingDto> CommunityStandings,
    IReadOnlyList<SupportPersonModerationStandingDto> ModerationStandings,
    IReadOnlyList<SupportPersonModerationHistoryDto> ModerationHistory,
    IReadOnlyList<SupportPersonTrustScoreDto> TrustScores,
    IReadOnlyList<SupportPersonEntitlementDto> Entitlements
);
