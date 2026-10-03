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
/// A cheermote fragment resolved to its render-ready image (chat-decoration spec §4): the scale-keyed CDN urls for the
/// tier the cheer qualified for, whether that image is animated, and the tier's colour. Null on the fragment until the
/// cheermote step resolves it from the cached Helix cheermotes; a miss leaves it null (the client falls back to text).
/// </summary>
/// <param name="Urls">Image links keyed by scale, from "1" (small) to "4" (large).</param>
/// <param name="Animated">True when the cheermote image is animated.</param>
/// <param name="ColorHex">The colour of the tier the cheer reached, as a hex colour such as #979797.</param>
public sealed record CheermoteImage(
    IReadOnlyDictionary<string, string> Urls,
    bool Animated,
    string ColorHex
);
