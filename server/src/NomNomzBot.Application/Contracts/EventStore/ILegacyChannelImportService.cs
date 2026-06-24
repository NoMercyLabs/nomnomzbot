// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Owner-gated one-shot backfill of a channel's history from the legacy NoMercy bot's SQLite database into the new
/// event journal, plus a rebuild of every read-model projection so the freshly-imported facts surface (the channel
/// event log, analytics dailies, viewer profiles, …). The import reads the legacy file READ-ONLY and appends to the
/// caller's tenant journal idempotently (dedup on a derived <c>EventId</c>), so a re-run imports nothing; the rebuild
/// resets each projection and replays the whole stream from position 0. Both are scoped to one tenant.
/// </summary>
public interface ILegacyChannelImportService
{
    /// <summary>
    /// Imports the legacy database's channel events onto <paramref name="broadcasterId"/>'s journal. Returns the
    /// import counts (read / imported / skipped-unmapped / skipped-duplicate). Fails if the legacy file is absent.
    /// </summary>
    Task<Result<LegacyImportResult>> ImportLegacyAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Rebuilds every registered projection for <paramref name="broadcasterId"/> from the journal (reset → replay from
    /// position 0). Returns the per-projection applied-event counts. Run after an import to surface the new facts.
    /// </summary>
    Task<Result<IReadOnlyList<ProjectionRebuildResult>>> RebuildProjectionsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The outcome of a legacy import, plus the journal head before/after so the caller can see the growth.</summary>
public sealed record LegacyImportResult(
    long TotalRead,
    long Imported,
    long SkippedUnmapped,
    long SkippedDuplicate,
    long HeadBefore,
    long HeadAfter
);

/// <summary>How many events one named projection applied during a rebuild.</summary>
public sealed record ProjectionRebuildResult(string ProjectionName, long EventsApplied);
