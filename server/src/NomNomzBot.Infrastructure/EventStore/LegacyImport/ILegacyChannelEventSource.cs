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

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// A forward-only stream of legacy <c>ChannelEvents</c> rows. Abstracted so the importer is decoupled from where the
/// rows come from — the production source reads the legacy bot's SQLite file (<see cref="LegacySqliteChannelEventSource"/>),
/// while tests can supply an in-memory sequence. Rows are yielded oldest-first so journal <c>StreamPosition</c>s land
/// in event order.
/// </summary>
public interface ILegacyChannelEventSource
{
    IAsyncEnumerable<LegacyChannelEventRow> ReadAllAsync(
        CancellationToken cancellationToken = default
    );
}
