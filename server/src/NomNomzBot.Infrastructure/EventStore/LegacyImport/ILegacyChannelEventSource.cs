// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
