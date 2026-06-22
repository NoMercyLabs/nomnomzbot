// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
public sealed record ChatEmote(
    EmoteProvider Provider,
    string Id,
    string Code,
    IReadOnlyDictionary<string, string> Urls,
    bool Animated,
    bool ZeroWidth,
    string? SetId = null,
    string? OwnerId = null,
    IReadOnlyList<string>? Formats = null
);
