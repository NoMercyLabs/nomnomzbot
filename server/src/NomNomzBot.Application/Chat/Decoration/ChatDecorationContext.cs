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

using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Moderation.ChatFilters;

namespace NomNomzBot.Application.Chat.Decoration;

/// <summary>
/// The mutable working state threaded through the decoration pipeline (chat-decoration spec §0/§3.1). The orchestrator
/// seeds it from the inbound <c>ChatMessageReceivedEvent</c>, then each
/// <see cref="NomNomzBot.Application.Chat.Services.IChatDecorationAdapter"/> mutates it in place. It carries no
/// enrichment logic; it is the substrate the adapters read and write. Fields are added as the adapters that consume them
/// land — the channel identity (tenant id + Twitch id/login) arrives with the emote-matching step that needs it.
/// </summary>
public sealed class ChatDecorationContext
{
    /// <summary>The message fragments, enriched in place as the pipeline runs (text exploded, emotes matched, urls filled).</summary>
    public List<ChatMessageFragment> Fragments { get; init; } = [];

    /// <summary>The owning channel's tenant id — the discriminator for per-channel cached Helix sets (badges, cheermotes).</summary>
    public Guid BroadcasterId { get; init; }

    /// <summary>
    /// The channel's Twitch broadcaster id — the discriminator for the per-channel cached emote sets.
    /// Empty when unknown, in which case channel-specific lookups simply miss and only global sets can match.
    /// </summary>
    public string TwitchBroadcasterId { get; init; } = string.Empty;

    /// <summary>The message's raw badges (set id + version id) seeded from the event; resolved to urls by the badge step.</summary>
    public IReadOnlyList<ChatBadge> Badges { get; init; } = [];

    /// <summary>The badges resolved to image urls — filled by <c>BadgeAdapter</c>, surfaced on the decorated message.</summary>
    public IReadOnlyList<ResolvedChatBadge> ResolvedBadges { get; set; } = [];

    /// <summary>
    /// Whether the sender may have inline HTML rendered: a live badge (subscriber and above), or the
    /// <see cref="ChatDecorationCapabilities.RenderHtml"/> capability (a <c>!permit</c> grant or a resolved level).
    /// </summary>
    public bool SenderMayRenderHtml { get; init; }

    /// <summary>
    /// Whether the sender's rendered HTML may keep a link to a host off <see cref="LinkAllowlist"/>: a moderator or
    /// broadcaster badge, or the <see cref="ChatDecorationCapabilities.LinkExternally"/> capability (a per-user grant
    /// or a resolved level). Without it such a link is unwrapped to its text.
    /// </summary>
    public bool SenderMayLinkExternally { get; init; }

    /// <summary>The channel's link allowlist; a link to one of its hosts is kept in rendered HTML for every sender.</summary>
    public LinkPolicy LinkAllowlist { get; init; } = LinkPolicy.Default;

    /// <summary>
    /// Whether the sender may trigger a link preview fetch: a live badge (subscriber and above), or the
    /// <see cref="ChatDecorationCapabilities.PreviewLinks"/> capability (a <c>!permit</c> grant or a resolved level).
    /// </summary>
    public bool SenderMayPreviewLinks { get; init; }

    /// <summary>
    /// The channel's enabled decoration feature keys (<c>use_bttv</c> / <c>use_ffz</c> / <c>use_7tv</c> /
    /// <c>use_link_preview</c>), resolved once by the orchestrator from the channel's rules so every adapter can gate
    /// synchronously in <c>AppliesTo</c> without an async feature lookup on the chat hot path.
    /// </summary>
    public IReadOnlySet<string> EnabledFeatures { get; init; } = new HashSet<string>();
}
