// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>Resolves the app-level YouTube Data API key on every call, so a key saved in the dashboard applies without a restart.</summary>
public interface IYouTubeApiKeyResolver
{
    /// <summary>The saved key first, then configuration; null when neither holds a non-blank key.</summary>
    Task<string?> GetAsync(CancellationToken cancellationToken = default);
}
