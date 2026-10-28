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

using NomNomzBot.Domain.Chat.Enums;

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// The single emote shape for EVERY provider (Twitch emotes use it too). The decorator fills
/// <see cref="Urls"/>/<see cref="Animated"/>/<see cref="ZeroWidth"/> so the client renders any emote
/// identically (chat-decoration spec §4). <see cref="Urls"/> is keyed by scale <c>"1".."4"</c>;
/// <see cref="ZeroWidth"/> (a 7TV overlay) lets the renderer stack this emote over the preceding one and is
/// false for the rest. <see cref="SetId"/>/<see cref="OwnerId"/>/<see cref="Formats"/> are populated for
/// <see cref="EmoteProvider.Twitch"/> only.
/// </summary>
/// <param name="Provider">The service the emote comes from, such as Twitch or 7TV.</param>
/// <param name="Id">The emote id at that service.</param>
/// <param name="Code">The text a viewer types to use the emote.</param>
/// <param name="Urls">Image links keyed by scale, from "1" (small) to "4" (large).</param>
/// <param name="Animated">True when the emote image is animated.</param>
/// <param name="ZeroWidth">True when the emote draws on top of the emote before it (a 7TV overlay).</param>
/// <param name="SetId">The Twitch emote set id. Set for Twitch emotes only.</param>
/// <param name="OwnerId">The user id of the channel that owns the emote. Set for Twitch emotes only.</param>
/// <param name="Formats">The image formats Twitch offers, such as static or animated. Set for Twitch emotes only.</param>
/// <param name="Type">
/// Twitch: the Helix emote_type (such as subscriptions, follower, bitstier, globals). Third party: "channel" or "global",
/// the set the emote came from. Null when the cache does not know it.
/// </param>
/// <param name="Tier">The subscription tier 1, 2 or 3 of a Twitch subscription emote. Null for every other emote.</param>
/// <param name="Owner">The owning channel (Twitch) or the uploader (7TV, BTTV, FFZ). Null when the cache does not know it.</param>
/// <param name="Artist">The artist when the provider names one apart from the owner (FFZ). Null otherwise.</param>
/// <param name="Origin">
/// Whose emote this is for the operator who asked for the catalogue. Set by the catalogue per request; null on a
/// cached emote and on the live feed.
/// </param>
/// <param name="OwnerChannel">
/// The channel the picker groups this emote under: the owning channel of a Twitch emote, or the viewed channel for a
/// 7TV, BTTV or FFZ channel set. Null for a global emote. Set by the catalogue per request.
/// </param>
/// <param name="StaticUrls">
/// Still-image links with the same scales as <see cref="Urls"/>, for a viewer who asks for reduced motion. An emote
/// that is not animated carries the same links as <see cref="Urls"/>. Null only on an entry cached before this field existed.
/// </param>
public sealed record ChatEmote(
    EmoteProvider Provider,
    string Id,
    string Code,
    IReadOnlyDictionary<string, string> Urls,
    bool Animated,
    bool ZeroWidth,
    string? SetId = null,
    string? OwnerId = null,
    IReadOnlyList<string>? Formats = null,
    string? Type = null,
    int? Tier = null,
    ChatEmoteOwner? Owner = null,
    ChatEmoteOwner? Artist = null,
    ChatEmoteOrigin? Origin = null,
    ChatEmoteOwner? OwnerChannel = null,
    IReadOnlyDictionary<string, string>? StaticUrls = null
);
