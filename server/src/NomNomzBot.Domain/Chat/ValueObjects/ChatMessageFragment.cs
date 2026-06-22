// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// A single fragment of a Twitch chat message.
/// Corresponds to one element in the EventSub channel.chat.message `fragments[]` array.
/// </summary>
public sealed class ChatMessageFragment
{
    /// <summary>Fragment type: "text" | "emote" | "cheermote" | "mention"</summary>
    public string Type { get; init; } = "text";

    /// <summary>The raw text of this fragment.</summary>
    public string Text { get; init; } = string.Empty;

    // ─── Emote fields (when Type == "emote") ─────────────────────────────────

    /// <summary>Twitch emote ID (e.g. "25" for Kappa).</summary>
    public string? EmoteId { get; init; }

    /// <summary>The emote set this emote belongs to.</summary>
    public string? EmoteSetId { get; init; }

    /// <summary>Channel that owns this emote (for channel-specific emotes).</summary>
    public string? EmoteOwnerId { get; init; }

    /// <summary>
    /// Available formats for this emote: "static" | "animated".
    /// Use "animated" if available, fall back to "static".
    /// </summary>
    public string[] EmoteFormats { get; init; } = [];

    /// <summary>
    /// The fully-resolved, render-ready emote for this fragment (Type == "emote"), unifying Twitch and
    /// third-party (BTTV/FFZ/7TV) emotes behind one shape — provider tag, scale-keyed urls, animated and
    /// zero-width flags (chat-decoration spec §4). Null until the decoration pipeline resolves it; the flat
    /// Twitch <c>Emote*</c> fields above carry the raw EventSub payload until then.
    /// </summary>
    public ChatEmote? Emote { get; init; }

    // ─── Cheermote fields (when Type == "cheermote") ─────────────────────────

    /// <summary>Cheermote prefix (e.g. "Cheer", "PogChamp").</summary>
    public string? CheermotePrefix { get; init; }

    /// <summary>Number of bits in this cheermote fragment.</summary>
    public int? CheermoteBits { get; init; }

    /// <summary>Cheer tier (1, 2, 3, 4, or 5) — determines animation and color.</summary>
    public int? CheermoteTier { get; init; }

    // ─── Mention fields (when Type == "mention") ──────────────────────────────

    /// <summary>Twitch user ID of the mentioned user.</summary>
    public string? MentionUserId { get; init; }

    /// <summary>Login (lowercase) of the mentioned user.</summary>
    public string? MentionUserLogin { get; init; }

    /// <summary>Display name of the mentioned user.</summary>
    public string? MentionUserName { get; init; }
}
