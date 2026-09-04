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
/// Resolves a 7TV paint id to the flattened <see cref="ChatPaint"/> the overlays render.
///
/// <para>
/// Paints are a small, slow-moving global set — about a thousand of them — and every chatter's paint is one
/// of that set. So this fetches the whole catalogue once and answers from memory, rather than making a
/// request per chatter: a busy chat resolves the same handful of paints thousands of times a stream, and a
/// per-message lookup would be both far slower and a good way to get rate-limited off 7TV entirely.
/// </para>
/// </summary>
public interface ISevenTvPaintCatalogue
{
    /// <summary>
    /// The paint for [paintId], or null when 7TV does not know it or the catalogue could not be loaded.
    /// Never throws: a chatter's cosmetic is decoration, and failing to fetch it must not cost them their
    /// message.
    /// </summary>
    Task<ChatPaint?> GetAsync(string paintId, CancellationToken cancellationToken = default);
}
