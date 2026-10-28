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
