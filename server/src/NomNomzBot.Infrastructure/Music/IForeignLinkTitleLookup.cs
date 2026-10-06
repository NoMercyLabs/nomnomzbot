// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
