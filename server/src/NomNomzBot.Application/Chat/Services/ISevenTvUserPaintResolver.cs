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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Resolves the 7TV paint a CHATTER (not a channel) is wearing, keyed by their Twitch user id.
///
/// <para>
/// Distinct from <see cref="ISevenTvPaintCatalogue"/>: the catalogue answers "what does paint X look like"
/// from the global set fetched once; this answers "which paint, if any, does THIS user wear" — a per-user
/// question that needs its own 7TV lookup (the user's <c>style.paint_id</c>) before the catalogue can be
/// asked anything.
/// </para>
/// </summary>
public interface ISevenTvUserPaintResolver
{
    /// <summary>
    /// The flattened paint [twitchUserId] wears, or null when they wear none, 7TV does not know them, or the
    /// lookup failed. Never throws: a chatter's cosmetic is decoration, and failing to fetch it must not cost
    /// them their message.
    /// </summary>
    Task<ChatPaint?> ResolveAsync(
        string twitchUserId,
        CancellationToken cancellationToken = default
    );
}
