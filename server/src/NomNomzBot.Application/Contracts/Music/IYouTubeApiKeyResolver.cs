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

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>Resolves the app-level YouTube Data API key on every call, so a key saved in the dashboard applies without a restart.</summary>
public interface IYouTubeApiKeyResolver
{
    /// <summary>The saved key first, then configuration; null when neither holds a non-blank key.</summary>
    Task<string?> GetAsync(CancellationToken cancellationToken = default);
}
