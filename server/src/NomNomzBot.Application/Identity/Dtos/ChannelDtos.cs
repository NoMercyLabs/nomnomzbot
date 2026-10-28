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

using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>Full channel detail for dashboard views.</summary>
public sealed record ChannelDto(
    string Id,
    string Name,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    bool IsOnboarded,
    string? Title,
    string? GameName,
    int? ViewerCount,
    DateTime? BotJoinedAt,
    string SubscriptionTier,
    string? Language,
    DateTime CreatedAt
);

/// <summary>
/// Lightweight channel info for lists and dropdowns. <c>Provider</c> is the streaming platform this channel
/// lives on (<c>AuthEnums.Platform</c>: twitch / youtube / kick), letting the dashboard tag each channel — and
/// each chat line / multi-watch pane — by its source. <c>ChatColor</c> is the streamer's Twitch chat color
/// (#RRGGBB), populated from <c>User.Color</c> when known — null until the first login after the color-sync
/// feature is deployed, or when the user has no color set; the dashboard uses it as the dynamic accent token
/// (design-system §2). <c>DeletedAt</c> and <c>PermanentAfter</c> are set only on a soft-deleted channel listed
/// inside its restore window: when it was deleted and the instant after which it can no longer be restored.
/// </summary>
public sealed record ChannelSummaryDto(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    string Role,
    int? ViewerCount,
    string Provider,
    string? ChatColor = null,
    ChannelSuspensionDto? Suspension = null,
    DateTime? DeletedAt = null,
    DateTime? PermanentAfter = null
);

/// <summary>
/// Why a channel is suspended (<c>Status</c> is suspended or platform_banned), since when, and the page its
/// people use to reach the platform. <c>ContactUrl</c> is null until a platform admin sets the support contact.
/// </summary>
public sealed record ChannelSuspensionDto(
    string Status,
    string? Reason,
    DateTime? SuspendedAt,
    string? ContactUrl
);

/// <summary>
/// One channel in the deployment's public directory (chat-client.md §9a "Reaching any channel"): only what
/// the channel shows every visitor on Twitch. No role, no settings, no tokens. <c>OnBot</c> is false for a
/// Twitch channel this deployment does not serve; its <c>Id</c> is then the Twitch user id, not a tenant Guid.
/// </summary>
public sealed record PublicChannelDto(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsLive,
    string Provider,
    bool OnBot = true
);

/// <summary>Lightweight channel info returned when looking up by overlay token.</summary>
public sealed record ChannelOverlayInfo(string BroadcasterId, string DisplayName);

/// <summary>Request to update channel settings.</summary>
public sealed record UpdateChannelSettingsDto
{
    public string? DisplayName { get; init; }
    public string? SubscriptionTier { get; init; }

    /// <summary>The command prefix (1-5 non-whitespace chars, e.g. <c>!</c>). Null leaves it unchanged.</summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// The visible marker bot-emitted lines carry when the bot posts through the streamer's own account
    /// (e.g. <c>*</c>, <c>#</c>, or an emoji; 1-4 chars). Empty string clears it back to none; null leaves
    /// it unchanged.
    /// </summary>
    public string? BotLinePrefix { get; init; }

    /// <summary>The channel's default language/locale (BCP-47, e.g. <c>en</c>). Null leaves it unchanged.</summary>
    public string? Locale { get; init; }

    /// <summary>Whether the bot auto-joins this channel's chat. Null leaves it unchanged.</summary>
    public bool? AutoJoin { get; init; }

    /// <summary>The streamer's IANA timezone (e.g. <c>Europe/Amsterdam</c>). Null leaves it unchanged.</summary>
    public string? Timezone { get; init; }

    /// <summary>
    /// Whether commands and rewards stay active while the channel is offline (default off: they switch off when
    /// the stream ends and back on when it starts). Null leaves it unchanged.
    /// </summary>
    public bool? KeepActiveWhileOffline { get; init; }

    /// <summary>
    /// The viewer message count from which <c>{user.activity.level}</c> reads <c>casual</c> (default 10). The three
    /// steps must satisfy 1 &lt;= casual &lt; regular &lt; veteran &lt;= 1,000,000, checked together with the
    /// stored values. Null leaves it unchanged.
    /// </summary>
    public int? ViewerCasualFrom { get; init; }

    /// <summary>The viewer message count from which the level reads <c>regular</c> (default 100). Null leaves it unchanged.</summary>
    public int? ViewerRegularFrom { get; init; }

    /// <summary>The viewer message count from which the level reads <c>veteran</c> (default 500). Null leaves it unchanged.</summary>
    public int? ViewerVeteranFrom { get; init; }
}

/// <summary>
/// The channel's onboarding "basics" — the settable-anywhere command <see cref="Prefix"/>, the default
/// <see cref="Locale"/>, the <see cref="AutoJoin"/> toggle, and the streamer's <see cref="Timezone"/>.
/// Backs both the Settings "Bot basics" card and the onboarding basics step.
/// </summary>
public sealed record ChannelBasicsDto(
    string Prefix,
    string? BotLinePrefix,
    string? Locale,
    bool AutoJoin,
    string? Timezone,
    bool KeepActiveWhileOffline = false,
    int ViewerCasualFrom = ViewerActivitySteps.DefaultCasualFrom,
    int ViewerRegularFrom = ViewerActivitySteps.DefaultRegularFrom,
    int ViewerVeteranFrom = ViewerActivitySteps.DefaultVeteranFrom
);

/// <summary>Request to create/onboard a new channel.</summary>
public sealed record CreateChannelRequest
{
    public required string BroadcasterId { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// A channel's built-in-command personality tone. <see cref="Personality"/> is the canonical tone token
/// (<c>PersonalityTone.*</c>); <see cref="Available"/> lists every selectable tone so a picker needs no
/// second call.
/// </summary>
public sealed record ChannelPersonalityDto(string Personality, IReadOnlyList<string> Available);

/// <summary>Request to set a channel's built-in-command personality tone.</summary>
public sealed record SetChannelPersonalityRequest
{
    /// <summary>One of <c>PersonalityTone.All</c> (case-insensitive).</summary>
    public required string Personality { get; init; }
}
