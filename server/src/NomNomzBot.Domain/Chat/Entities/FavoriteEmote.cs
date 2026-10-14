// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Entities;

/// <summary>
/// One emote a signed-in user starred in the dashboard chat emote picker. Per user, not per channel, so
/// the favorites follow the user to every device and every channel (as Twitch favorites do). Unique on
/// <c>(UserId, Provider, EmoteId)</c>; at most <c>FavoriteEmoteLimits.MaxPerUser</c> rows per user.
/// Not soft-deletable: un-starring removes the row.
/// </summary>
public class FavoriteEmote : BaseEntity
{
    public Guid Id { get; set; } = MonotonicGuid.Create();

    /// <summary>The signed-in user (<c>User.Id</c>) who owns the favorite.</summary>
    public Guid UserId { get; set; }

    /// <summary>The emote provider: <c>Twitch</c>, <c>SevenTv</c>, <c>Bttv</c>, <c>Ffz</c> or <c>Emoji</c>.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>The provider's emote id. For <c>Emoji</c> the glyph itself.</summary>
    public string EmoteId { get; set; } = null!;

    /// <summary>The text that goes into chat when the emote is used.</summary>
    public string Code { get; set; } = null!;

    /// <summary>
    /// A snapshot of the emote image, so a favorite still renders where the current emote list lacks it.
    /// Null for an emoji.
    /// </summary>
    public string? ImageUrl { get; set; }
}
