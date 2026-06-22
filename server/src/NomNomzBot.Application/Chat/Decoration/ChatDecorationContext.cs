// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Chat.ValueObjects;

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

    /// <summary>
    /// The channel's Twitch broadcaster id — the discriminator for the per-channel cached emote/badge/cheermote sets.
    /// Empty when unknown, in which case channel-specific lookups simply miss and only global sets can match.
    /// </summary>
    public string TwitchBroadcasterId { get; init; } = string.Empty;

    /// <summary>
    /// The channel's enabled decoration feature keys (<c>use_bttv</c> / <c>use_ffz</c> / <c>use_7tv</c> /
    /// <c>use_link_preview</c>), resolved once by the orchestrator from the channel's rules so every adapter can gate
    /// synchronously in <c>AppliesTo</c> without an async feature lookup on the chat hot path.
    /// </summary>
    public IReadOnlySet<string> EnabledFeatures { get; init; } = new HashSet<string>();
}
