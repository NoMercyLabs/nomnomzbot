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
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// One-shot importer that maps the legacy NoMercy bot's <c>ChannelEvents</c> rows onto the new domain events and
/// appends them to the journal as <c>Source="import"</c>, so a projection rebuild reconstructs the owner's real
/// channel history with NO live Twitch call. Mapping is delegated to <see cref="LegacyChannelEventMapper"/> (pure);
/// this type owns the I/O: it reads rows in bounded batches from any <see cref="ILegacyChannelEventSource"/>, maps
/// each to an <see cref="AppendEventRequest"/>, and writes through <see cref="IEventJournal.AppendBatchAsync"/>
/// (one transaction per batch, contiguous per-tenant <c>StreamPosition</c>s, idempotent on the derived
/// <c>EventId</c>). Re-running is safe: a row whose <c>EventId</c> already exists is counted as a duplicate skip and
/// consumes no new position.
/// </summary>
public sealed class LegacyChannelEventImporter
{
    private const int BatchSize = 500;

    private readonly IEventJournal _journal;
    private readonly LegacyChannelEventMapper _mapper;

    public LegacyChannelEventImporter(IEventJournal journal, LegacyChannelEventMapper mapper)
    {
        _journal = journal;
        _mapper = mapper;
    }

    /// <summary>
    /// Imports every row the <paramref name="source"/> yields onto <paramref name="targetBroadcasterId"/>'s journal.
    /// Rows of an unimported type/shape are skipped (mapper returns null); rows already present are skipped as
    /// duplicates. A journal append failure aborts and returns the failure (the failed batch rolls back atomically).
    /// </summary>
    public async Task<Result<LegacyImportSummary>> ImportAsync(
        Guid targetBroadcasterId,
        ILegacyChannelEventSource source,
        CancellationToken cancellationToken = default
    )
    {
        long totalRead = 0;
        long imported = 0;
        long skippedUnmapped = 0;
        long skippedDuplicate = 0;

        List<AppendEventRequest> pending = new(BatchSize);

        await foreach (LegacyChannelEventRow row in source.ReadAllAsync(cancellationToken))
        {
            totalRead++;

            AppendEventRequest? request = _mapper.Map(row, targetBroadcasterId);
            if (request is null)
            {
                skippedUnmapped++;
                continue;
            }

            // Up-front dedupe so a re-run reports an honest duplicate count; AppendBatchAsync would also no-op the
            // duplicate, but probing here lets the summary distinguish "already imported" from "newly written".
            Result<EventRecord> existing = await _journal.GetByEventIdAsync(
                request.EventId,
                cancellationToken
            );
            if (existing.IsSuccess)
            {
                skippedDuplicate++;
                continue;
            }

            pending.Add(request);
            if (pending.Count < BatchSize)
                continue;

            Result flush = await FlushAsync(pending, cancellationToken);
            if (flush.IsFailure)
                return Result.Failure<LegacyImportSummary>(
                    flush.ErrorMessage!,
                    flush.ErrorCode,
                    flush.ErrorDetail
                );

            imported += pending.Count;
            pending.Clear();
        }

        if (pending.Count > 0)
        {
            Result flush = await FlushAsync(pending, cancellationToken);
            if (flush.IsFailure)
                return Result.Failure<LegacyImportSummary>(
                    flush.ErrorMessage!,
                    flush.ErrorCode,
                    flush.ErrorDetail
                );

            imported += pending.Count;
        }

        return Result.Success(
            new LegacyImportSummary(totalRead, imported, skippedUnmapped, skippedDuplicate)
        );
    }

    private async Task<Result> FlushAsync(
        IReadOnlyList<AppendEventRequest> batch,
        CancellationToken cancellationToken
    )
    {
        Result<IReadOnlyList<EventRecord>> appended = await _journal.AppendBatchAsync(
            batch,
            cancellationToken
        );
        return appended.IsFailure
            ? Result.Failure(appended.ErrorMessage!, appended.ErrorCode, appended.ErrorDetail)
            : Result.Success();
    }
}
