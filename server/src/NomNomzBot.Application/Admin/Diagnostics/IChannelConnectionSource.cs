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

using NomNomzBot.Application.Admin.Dtos;

namespace NomNomzBot.Application.Admin.Diagnostics;

/// <summary>
/// One place the code base stores a channel's connections (the vault, the Discord links, OBS, ...). The
/// <c>connections</c> diagnostics section asks every registered source for its rows. A new store is a new class;
/// the section never changes.
/// </summary>
public interface IChannelConnectionSource
{
    /// <summary>The channel's rows from this store, with health states only and never a token or secret.</summary>
    Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
