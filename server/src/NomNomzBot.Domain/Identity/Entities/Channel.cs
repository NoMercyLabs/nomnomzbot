// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class Channel : SoftDeletableEntity
{
    // Surrogate UUIDv7 PK (schema §1.1, A.2) = the tenant id used in every BroadcasterId + RLS.
    // Generated app-side; never DB-default; never sent to Twitch.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Broadcaster identity — one channel per owner (schema A.2). Replaces the old
    // [ForeignKey(nameof(Id))] shared-PK hack between Channel and User.
    public Guid OwnerUserId { get; set; }

    // External Twitch channel/broadcaster id — first-class indexed attribute (schema A.2), NOT the key.
    // Nullable projection (platform-identity §1): filled iff Provider=twitch; a YouTube/Kick channel uses
    // ExternalChannelId instead. Every Helix call's broadcaster_id resolves to this from the tenant Guid.
    [MaxLength(50)]
    public string? TwitchChannelId { get; set; }

    // Streaming platform this channel lives on ([VC:enum] AuthEnums.Platform, platform-identity §1). Backfilled
    // 'twitch'; a YouTube/Kick presence is a separate Channel row (tenant) under the same owner.
    [MaxLength(20)]
    public string Provider { get; set; } = AuthEnums.Platform.Twitch;

    // The platform's own channel/broadcaster id — equals TwitchChannelId for a Twitch channel. The stable
    // cross-platform key: (Provider, ExternalChannelId) uniquely identifies a channel on any platform.
    [MaxLength(100)]
    public string ExternalChannelId { get; set; } = "";

    [MaxLength(25)]
    public string Name { get; set; } = null!;

    // Lower-cased Name for case-insensitive lookup (IRC keys by login name, schema A.2).
    [MaxLength(25)]
    public string NameNormalized { get; set; } = null!;

    // Tenant lifecycle ([VC:enum], schema A.2).
    [MaxLength(20)]
    public string Status { get; set; } = AuthEnums.ChannelStatus.Active;

    public DateTime? SuspendedAt { get; set; }

    [MaxLength(500)]
    public string? SuspendedReason { get; set; }

    // Deployment profile this tenant runs under ([VC:enum], schema A.2).
    [MaxLength(20)]
    public string DeploymentMode { get; set; } = AuthEnums.DeploymentMode.Saas;

    // Resolved billing tier key (cross-ref monetization.md); the entitlement source for gated features.
    [MaxLength(20)]
    public string BillingTierKey { get; set; } = "free";

    public bool Enabled { get; set; } = true;

    [MaxLength(450)]
    [TemplatedUserContent]
    public string? ShoutoutTemplate { get; set; }

    public DateTime? LastShoutout { get; set; }

    public int ShoutoutInterval { get; set; } = 10;

    [MaxLength(100)]
    public string? UsernamePronunciation { get; set; }

    /// <summary>
    /// The channel's built-in-command voice ([VC:enum] <see cref="PersonalityTone"/>). Every built-in that
    /// has a response renders a tone-appropriate, varied template from the tone catalog. Defaults to
    /// <see cref="PersonalityTone.Informative"/> (clear + polite) for every new and existing channel.
    /// </summary>
    [MaxLength(20)]
    public string Personality { get; set; } = PersonalityTone.Informative;

    /// <summary>
    /// The prefix that marks a chat message as a command (e.g. <c>!</c> in <c>!uptime</c>). One-to-five
    /// non-whitespace characters; the chat hot path (<c>ChatMessageHandler</c>) reads it to decide whether a
    /// message is a command, so a change applies live once the registry reloads the channel's settings.
    /// Defaults to <c>!</c> for every new and existing channel.
    /// </summary>
    [MaxLength(5)]
    public string CommandPrefix { get; set; } = "!";

    /// <summary>
    /// The user-defined marker a bot-emitted chat line is prefixed with (product decision D5) so viewers
    /// can visually tell the bot's voice apart from the streamer's own typing when the bot has no dedicated
    /// account and posts through the streamer's own (self-host default, <c>BotIdentityType.None</c>). One to
    /// four characters (covers a single emoji, including multi-codepoint sequences); null/empty = no visible
    /// prefix. This is entirely separate from the <c>BotEmittedLine.Marker</c> loop-guard stamp — that is an
    /// invisible marker applied to every bot line regardless of account; this is a visible, opt-in courtesy
    /// marker applied only on the streamer's-own-account configuration.
    /// </summary>
    [MaxLength(16)]
    public string? BotLinePrefix { get; set; }

    public bool IsOnboarded { get; set; }

    public DateTime? BotJoinedAt { get; set; }

    [MaxLength(36)]
    public string OverlayToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Opaque, rotatable public song-request-page token (music-sr.md §3.7) — resolves the public
    /// <c>/sr/{token}</c> page to this channel without a JWT. Distinct from <see cref="OverlayToken"/> (OBS
    /// sources). Null until first minted by <c>ISongRequestPageTokenService.GetOrCreateAsync</c>. Not PII.
    /// </summary>
    [MaxLength(64)]
    public string? SongRequestPageToken { get; set; }

    public bool IsLive { get; set; }

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(50)]
    public string? GameId { get; set; }

    [MaxLength(255)]
    public string? GameName { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    public int StreamDelay { get; set; }

    public List<string> Tags { get; set; } = [];
    public List<string> ContentLabels { get; set; } = [];

    public bool IsBrandedContent { get; set; }

    /// <summary>
    /// Opt-in (default OFF, house rule: opt-in/default-deny) — when true, the bot posts a short
    /// tone-resolved "I'm online" announcement in this channel's chat the moment it successfully
    /// joins/connects (<see cref="NomNomzBot.Application.Identity.Services.IChannelService.JoinAsync"/>).
    /// Distinct from the operator-configured <c>stream.online</c> event response (that fires on the
    /// STREAM going live; this fires on the BOT's own connect), so existing channels are never
    /// surprised by a new unsolicited message until they turn it on.
    /// </summary>
    public bool AnnounceOnConnect { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public virtual User User { get; set; } = null!;

    public virtual ICollection<ChannelModerator> Moderators { get; set; } = [];
    public virtual ICollection<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams { get; set; } =
    [];
    public virtual ICollection<ChannelEvent> Events { get; set; } = [];
}
