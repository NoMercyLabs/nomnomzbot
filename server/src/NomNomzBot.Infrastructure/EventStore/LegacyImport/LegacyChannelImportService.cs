// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.Analytics;

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Orchestrates the owner-gated legacy backfill (event-store §3.4): it wires the pure <see cref="LegacyChannelEventImporter"/>
/// to the live journal and the <see cref="IProjectionRunner"/>, exposing the two operator actions — import the legacy
/// channel events onto the tenant's journal, then rebuild every projection from the journal so the new facts surface.
/// The legacy SQLite file is opened READ-ONLY (<see cref="LegacySqliteChannelEventSource"/>); the live database is
/// never reset and the owner is never logged out — only journal rows are appended and read-model rows rebuilt.
/// </summary>
public sealed class LegacyChannelImportService(
    IEventJournal journal,
    IProjectionRunner projectionRunner,
    IEnumerable<IProjection> projections,
    ILegacyDatabaseLocator legacyDatabase,
    ChannelEventActorBackfill actorBackfill,
    ILogger<LegacyChannelImportService> logger
) : ILegacyChannelImportService
{
    public async Task<Result<LegacyImportResult>> ImportLegacyAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Result<string> path = legacyDatabase.Resolve();
        if (path.IsFailure)
            return Result.Failure<LegacyImportResult>(path.ErrorMessage!, path.ErrorCode);

        Result<long> headBefore = await journal.GetHeadPositionAsync(
            broadcasterId,
            cancellationToken
        );
        if (headBefore.IsFailure)
            return Result.Failure<LegacyImportResult>(
                headBefore.ErrorMessage!,
                headBefore.ErrorCode
            );

        LegacyChannelEventImporter importer = new(journal, new LegacyChannelEventMapper(), logger);
        LegacySqliteChannelEventSource source = new(path.Value);

        Result<LegacyImportSummary> imported = await importer.ImportAsync(
            broadcasterId,
            source,
            cancellationToken
        );
        if (imported.IsFailure)
            return Result.Failure<LegacyImportResult>(imported.ErrorMessage!, imported.ErrorCode);

        Result<long> headAfter = await journal.GetHeadPositionAsync(
            broadcasterId,
            cancellationToken
        );
        if (headAfter.IsFailure)
            return Result.Failure<LegacyImportResult>(headAfter.ErrorMessage!, headAfter.ErrorCode);

        LegacyImportSummary summary = imported.Value;
        return Result.Success(
            new LegacyImportResult(
                summary.TotalRead,
                summary.Imported,
                summary.SkippedUnmapped,
                summary.SkippedDuplicate,
                headBefore.Value,
                headAfter.Value,
                summary.SkippedByLegacyType
            )
        );
    }

    public async Task<Result<IReadOnlyList<ProjectionRebuildResult>>> RebuildProjectionsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ProjectionRebuildResult> results = [];

        // Rebuild only the tenant-scoped projections for this broadcaster; global projections fold the cross-tenant
        // stream and are not a per-channel operator action (a self-host install has the one tenant regardless).
        foreach (IProjection projection in projections.Where(p => !p.IsGlobal))
        {
            Result<long> rebuilt = await projectionRunner.RebuildAsync(
                projection.Name,
                broadcasterId,
                cancellationToken
            );
            if (rebuilt.IsFailure)
                return Result.Failure<IReadOnlyList<ProjectionRebuildResult>>(
                    $"Rebuild of projection '{projection.Name}' failed: {rebuilt.ErrorMessage}",
                    rebuilt.ErrorCode
                );

            results.Add(new ProjectionRebuildResult(projection.Name, rebuilt.Value));
        }

        // After the channel-event-log projection has rebuilt every row (with the actor's Twitch id snapshotted in
        // Data), link each row to its internal User in one set-based pass — get-or-create the actors in bulk, then
        // one UPDATE per distinct actor. This is the cheap replacement for a per-event lookup during the fold, and
        // it makes ChannelEvents.UserId (and the dashboard's distinct-viewer count) real with NO external call.
        Result<long> backfilled = await actorBackfill.BackfillAsync(
            broadcasterId,
            cancellationToken
        );
        if (backfilled.IsFailure)
            return Result.Failure<IReadOnlyList<ProjectionRebuildResult>>(
                $"Channel-event actor backfill failed: {backfilled.ErrorMessage}",
                backfilled.ErrorCode
            );

        results.Add(new ProjectionRebuildResult("channel-event-actor-backfill", backfilled.Value));

        return Result.Success<IReadOnlyList<ProjectionRebuildResult>>(results);
    }
}
