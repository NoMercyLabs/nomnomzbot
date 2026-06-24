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
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger _logger;

    public LegacyChannelEventImporter(
        IEventJournal journal,
        LegacyChannelEventMapper mapper,
        ILogger? logger = null
    )
    {
        _journal = journal;
        _mapper = mapper;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Imports every row the <paramref name="source"/> yields onto <paramref name="targetBroadcasterId"/>'s journal.
    /// Rows of an unimported type/shape are skipped (mapper returns null); rows already present are skipped as
    /// duplicates. A journal append failure aborts and returns the failure (the failed batch rolls back atomically).
    /// The optional <paramref name="readProgress"/> reports the cumulative rows read+mapped during the streaming read,
    /// and <paramref name="appendProgress"/> reports the cumulative rows appended after each flushed batch — so a
    /// long-running standalone import is observable and a stall is pinpointed to a phase + count, never silent.
    /// </summary>
    public async Task<Result<LegacyImportSummary>> ImportAsync(
        Guid targetBroadcasterId,
        ILegacyChannelEventSource source,
        CancellationToken cancellationToken = default,
        IProgress<long>? readProgress = null,
        IProgress<long>? appendProgress = null
    )
    {
        long totalRead = 0;
        long imported = 0;
        long skippedDuplicate = 0;
        Dictionary<string, long> skippedByType = new(StringComparer.Ordinal);

        // Map every row first. The legacy history is bounded (~41.5k small requests), so collecting the mapped set
        // lets us dedupe in ONE bulk query instead of a per-row probe — the difference between seconds and minutes.
        List<AppendEventRequest> mapped = [];
        await foreach (LegacyChannelEventRow row in source.ReadAllAsync(cancellationToken))
        {
            totalRead++;
            if (totalRead % 5000 == 0)
                readProgress?.Report(totalRead);
            AppendEventRequest? request = _mapper.Map(row, targetBroadcasterId);
            if (request is null)
            {
                skippedByType[row.Type] = skippedByType.GetValueOrDefault(row.Type) + 1;
                continue;
            }
            mapped.Add(request);
        }
        readProgress?.Report(totalRead);

        long skippedUnmapped = skippedByType.Values.Sum();

        // One bulk existence check across all mapped ids → an honest duplicate count and a re-run that writes nothing.
        Result<IReadOnlySet<Guid>> existingResult = await _journal.GetExistingEventIdsAsync(
            mapped.Select(r => r.EventId).ToList(),
            cancellationToken
        );
        if (existingResult.IsFailure)
            return Result.Failure<LegacyImportSummary>(
                existingResult.ErrorMessage!,
                existingResult.ErrorCode
            );
        IReadOnlySet<Guid> existing = existingResult.Value;

        // Skip ids already in the journal (re-run idempotency) AND any duplicate id within this run (a real Twitch
        // id appearing twice), counting both as duplicates so the summary stays honest and StreamPositions contiguous.
        HashSet<Guid> seen = [];
        List<AppendEventRequest> pending = new(BatchSize);
        foreach (AppendEventRequest request in mapped)
        {
            if (existing.Contains(request.EventId) || !seen.Add(request.EventId))
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
            appendProgress?.Report(imported);
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
            appendProgress?.Report(imported);
        }

        // Account for every legacy row precisely — no silent drops. The per-type skip breakdown lets the operator
        // confirm the skips are intended (definition CRUD, progress ticks, reward update churn, websocket noise, the
        // rare anonymous-chatter notice) rather than a mapping gap.
        if (skippedByType.Count > 0)
            _logger.LogInformation(
                "Legacy import for {Tenant}: read {TotalRead}, imported {Imported}, "
                    + "skipped-duplicate {SkippedDuplicate}, skipped-unmapped {SkippedUnmapped} by type {SkippedByType}",
                targetBroadcasterId,
                totalRead,
                imported,
                skippedDuplicate,
                skippedUnmapped,
                string.Join(
                    ", ",
                    skippedByType
                        .OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{kv.Key}={kv.Value}")
                )
            );

        return Result.Success(
            new LegacyImportSummary(
                totalRead,
                imported,
                skippedUnmapped,
                skippedDuplicate,
                skippedByType
            )
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
