// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Discord;

// ── Guild connection ────────────────────────────────────────────────────────

/// <summary>Read model of a Discord guild link (discord.md §4). Ids are <c>Guid</c>; Discord ids are <c>string</c>.</summary>
public sealed record DiscordGuildConnectionDto(
    Guid Id,
    Guid BroadcasterId,
    string GuildId,
    string? GuildName,
    bool BotInstalled,
    string ServerConsentStatus,
    string? ApprovedByDiscordUserId,
    DateTime? ApprovedAt,
    bool StreamerEnabled,
    bool IsLinkActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Carried out of the OAuth callback into <c>IDiscordGuildService.UpsertFromOAuthAsync</c>.</summary>
public sealed record DiscordGuildOAuthResult(
    string GuildId,
    string? GuildName,
    string AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? InstalledByDiscordUserId
);

// ── Notification config ─────────────────────────────────────────────────────

public sealed record DiscordNotificationConfigDto(
    Guid Id,
    Guid GuildConnectionId,
    string TriggerType,
    bool Enabled,
    string TargetChannelId,
    Guid? PingRoleId,
    string? MessageTemplate,
    DiscordEmbedDto? EmbedConfig,
    string? MilestoneType,
    int? MilestoneThreshold,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateDiscordNotificationConfigRequest(
    string TriggerType,
    bool Enabled,
    string TargetChannelId,
    Guid? PingRoleId,
    string? MessageTemplate,
    DiscordEmbedDto? EmbedConfig,
    string? MilestoneType,
    int? MilestoneThreshold
);

public sealed record UpdateDiscordNotificationConfigRequest(
    bool Enabled,
    string TargetChannelId,
    Guid? PingRoleId,
    string? MessageTemplate,
    DiscordEmbedDto? EmbedConfig,
    string? MilestoneType,
    int? MilestoneThreshold
);

/// <summary>The <c>[VC:JSON]</c> embed shape — mirrors the Domain <c>DiscordEmbedConfig</c> on the wire.</summary>
public sealed record DiscordEmbedDto(
    string? Title,
    string? Description,
    string? Color,
    string? ThumbnailUrl,
    string? ImageUrl,
    string? FooterText,
    IReadOnlyList<DiscordEmbedFieldDto>? Fields
);

public sealed record DiscordEmbedFieldDto(string Name, string Value, bool Inline);

public sealed record DiscordNotificationPreviewDto(
    string RenderedContent,
    DiscordEmbedDto? RenderedEmbed,
    string? PingRoleMention
);

// ── Notify role + opt-in ────────────────────────────────────────────────────

public sealed record DiscordNotificationRoleDto(
    Guid Id,
    Guid GuildConnectionId,
    string DiscordRoleId,
    string? RoleName,
    bool SelfAssignEnabled,
    string? ButtonMessageId,
    string? ButtonChannelId,
    int OptInCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateDiscordNotificationRoleRequest(
    string DiscordRoleId,
    string? RoleName,
    bool SelfAssignEnabled
);

public sealed record UpdateDiscordNotificationRoleRequest(string? RoleName, bool SelfAssignEnabled);

public sealed record DiscordMemberOptInRequest(string DiscordMemberId, string Source);

// ── Dispatch ────────────────────────────────────────────────────────────────

public sealed record DiscordDispatchRequest(
    Guid BroadcasterId,
    string TriggerType,
    string DedupeKey,
    Guid? StreamId,
    IReadOnlyDictionary<string, string> TemplateData
);

public sealed record DiscordDispatchOutcomeDto(
    Guid DispatchId,
    string Status,
    string? PostedMessageId,
    string? Error
);

public sealed record DiscordDispatchLogDto(
    Guid Id,
    Guid NotificationConfigId,
    string TriggerType,
    string DedupeKey,
    Guid? StreamId,
    string? PostedMessageId,
    string Status,
    string? Error,
    DateTime DispatchedAt
);

// ── Gateway value objects (Infrastructure-internal payloads) ─────────────────

public sealed record DiscordOutboundMessage(
    string Content,
    DiscordEmbedDto? Embed,
    string? PingRoleId
);

public sealed record DiscordOptInButton(
    string MessageContent,
    Guid NotificationRoleId,
    string ButtonLabel
);

// ── Guild directory (live pickers — ROADMAP guild read endpoints) ────────────

/// <summary>The linked guild's live profile, proxied from Discord for the dashboard.</summary>
public sealed record DiscordGuildInfoDto(string Id, string Name, string? Icon, string? Description);

/// <summary>A live guild role, for the notify-role picker. <c>Managed</c> roles cannot be self-assigned.</summary>
public sealed record DiscordGuildRoleDto(
    string Id,
    string Name,
    int Color,
    int Position,
    bool Managed
);

/// <summary>A live guild channel, for the target/button channel pickers. <c>Type</c> is Discord's channel type (0 = text).</summary>
public sealed record DiscordGuildChannelDto(
    string Id,
    string? Name,
    int Type,
    string? ParentId,
    int Position
);

// ── Controller request DTOs (discord.md §5) ──────────────────────────────────

public sealed record ServerConsentRequest(string ApprovedByDiscordUserId);

public sealed record StreamerEnabledRequest(bool Enabled);

public sealed record PostOptInButtonRequest(string ButtonChannelId);
