// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
    string? InstalledByDiscordUserId,
    // The Twitch account ids linked to the installer's Discord profile. Null when unknown; the installer then
    // counts as given. An empty list or a list without the channel's Twitch id means the installer is not the
    // streamer, so their Discord id is not recorded.
    IReadOnlyList<string>? LinkedTwitchIds = null
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
    string TargetChannelId,
    Guid? PingRoleId,
    string? MessageTemplate,
    DiscordEmbedDto? EmbedConfig,
    string? MilestoneType,
    int? MilestoneThreshold,
    bool Enabled = Domain.Discord.DiscordConfigDefaults.NotificationEnabled
);

// ── Live-role rule ──────────────────────────────────────────────────────────

public sealed record DiscordLiveRoleConfigDto(
    Guid Id,
    Guid GuildConnectionId,
    string RoleId,
    string DiscordMemberId,
    bool Enabled,
    bool IsCurrentlyApplied,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// DiscordMemberId is optional: left out, the role goes to the Discord account Discord has linked to the
// streamer's Twitch (recorded when the bot was added). Older clients that name a member keep working.
public sealed record CreateDiscordLiveRoleConfigRequest(
    string RoleId,
    string? DiscordMemberId = null,
    bool Enabled = Domain.Discord.DiscordConfigDefaults.LiveRoleEnabled
);

public sealed record UpdateDiscordLiveRoleConfigRequest(
    string RoleId,
    string? DiscordMemberId,
    bool Enabled
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
    IReadOnlyList<DiscordEmbedFieldDto>? Fields,
    string? TitleUrl = null,
    string? AuthorName = null,
    string? AuthorIconUrl = null,
    string? AuthorUrl = null,
    string? FooterIconUrl = null,
    bool ShowTimestamp = false,
    string? ButtonLabel = null,
    string? ButtonUrl = null
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
    // This role also DELIVERS BY DM: opted-in members each get the go-live notification as a direct
    // message (decided 2026-07-17).
    bool DmEnabled,
    string? ButtonMessageId,
    string? ButtonChannelId,
    int OptInCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateDiscordNotificationRoleRequest(
    string DiscordRoleId,
    string? RoleName,
    bool SelfAssignEnabled,
    bool DmEnabled = false
);

public sealed record UpdateDiscordNotificationRoleRequest(
    string? RoleName,
    bool SelfAssignEnabled,
    bool DmEnabled = false
);

public sealed record DiscordMemberOptInRequest(string DiscordMemberId, string Source);

// ── Triggers ────────────────────────────────────────────────────────────────

/// <summary>One event the server can post to Discord for, and the placeholders its message may use.</summary>
public sealed record DiscordNotificationTriggerDto(
    string Name,
    string LabelKey,
    IReadOnlyList<string> Placeholders
);

// ── Dispatch ────────────────────────────────────────────────────────────────

public sealed record DiscordDispatchRequest(
    Guid BroadcasterId,
    string TriggerType,
    string DedupeKey,
    Guid? StreamId,
    IReadOnlyDictionary<string, string> TemplateData,
    DateTimeOffset? LiveSince = null
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
    string? PingRoleId,
    DateTimeOffset? SentAt = null
);

public sealed record DiscordOptInButton(
    string MessageContent,
    Guid NotificationRoleId,
    string ButtonLabel
);

// ── Guild directory (live pickers — ROADMAP guild read endpoints) ────────────

/// <summary>The linked guild's live profile, proxied from Discord for the dashboard.</summary>
public sealed record DiscordGuildInfoDto(string Id, string Name, string? Icon, string? Description);

/// <summary>Whether the bot is a member of the linked server right now, as Discord answers.</summary>
public sealed record DiscordBotPresenceDto(Guid ConnectionId, bool BotInstalled);

/// <summary>A live guild role, for the notify-role picker. <c>Managed</c> roles cannot be self-assigned.
/// <see cref="Permissions"/> is Discord's decimal permission bitfield as a string (53-bit-safe wire format).</summary>
public sealed record DiscordGuildRoleDto(
    string Id,
    string Name,
    int Color,
    int Position,
    bool Managed,
    bool Mentionable = false,
    string Permissions = "0"
);

/// <summary>
/// A live guild member, for the member pickers. <see cref="DisplayName"/> is the name the guild shows (nickname,
/// then global name, then username); <see cref="AvatarUrl"/> is the Discord CDN avatar, null when the member has none.
/// </summary>
public sealed record DiscordGuildMemberDto(
    string Id,
    string Username,
    string DisplayName,
    string? AvatarUrl
);

/// <summary>A live guild channel, for the target/button channel pickers. <c>Type</c> is Discord's channel type (0 = text).</summary>
public sealed record DiscordGuildChannelDto(
    string Id,
    string? Name,
    int Type,
    string? ParentId,
    int Position
);

/// <summary>
/// A live guild role carrying whether the BOT CAN ASSIGN IT right now (S055c) — the hierarchy/permission check
/// from <see cref="IDiscordBotGateway.ValidateRoleAssignableAsync"/>, evaluated once for every role in the guild
/// instead of one Discord round-trip per role. <see cref="CanAssign"/> false always carries a non-null
/// <see cref="UnavailableReasonCode"/>/<see cref="UnavailableReason"/> (<c>DISCORD_MISSING_MANAGE_ROLES</c> or
/// <c>DISCORD_ROLE_HIERARCHY</c>) — never a bare disabled flag with no explanation.
/// </summary>
public sealed record DiscordAssignableRoleDto(
    string Id,
    string Name,
    int Color,
    int Position,
    bool Managed,
    bool Mentionable,
    string Permissions,
    bool CanAssign,
    string? UnavailableReasonCode,
    string? UnavailableReason
);

/// <summary>
/// A live guild channel carrying whether the BOT CAN POST IN IT right now (S055c) — computed from the bot's
/// effective permissions in that specific channel, including per-channel permission overwrites (a channel
/// overwrite can deny View Channel/Send Messages even when the bot's guild-level role permissions would allow
/// it). <see cref="CanPost"/> false always carries a non-null <see cref="UnavailableReasonCode"/>/
/// <see cref="UnavailableReason"/>.
/// </summary>
public sealed record DiscordPostableChannelDto(
    string Id,
    string? Name,
    int Type,
    string? ParentId,
    int Position,
    bool CanPost,
    string? UnavailableReasonCode,
    string? UnavailableReason
);

// ── Controller request DTOs (discord.md §5) ──────────────────────────────────

public sealed record ServerConsentRequest(string ApprovedByDiscordUserId);

public sealed record StreamerEnabledRequest(bool Enabled);

public sealed record PostOptInButtonRequest(string ButtonChannelId);
