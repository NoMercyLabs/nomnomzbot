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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Reads the title of a link the active music provider does not own, so the request can be searched by
/// name instead of by URL.
/// </summary>
public interface IForeignLinkTitleLookup
{
    /// <summary>
    /// The title behind <paramref name="link"/>, or null when the link is not one this lookup understands
    /// (it makes no network call then) or the title could not be fetched for any reason.
    /// </summary>
    Task<string?> TryGetTitleAsync(string link, CancellationToken cancellationToken);
}
