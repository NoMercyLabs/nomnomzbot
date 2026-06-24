// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Portable JSONL export/import of one tenant's journal slice (<see cref="IEventJournalPortabilityService"/>).
/// EXPORT reads the tenant stream forward in bounded batches via <see cref="IEventJournal.ReadStreamAsync"/> and
/// writes one <see cref="EventJournalExportLine"/> per line, so the whole journal never sits in memory. IMPORT
/// parses each line, brings its payload to the current shape through the <see cref="IEventUpcasterRegistry"/>,
/// re-tenants the event to the target broadcaster, and appends the batch through
/// <see cref="IEventJournal.AppendBatchAsync"/> — which runs in one <c>IUnitOfWork</c> transaction, allocates
/// fresh per-tenant <c>StreamPosition</c>s, and is idempotent on <c>EventId</c>. Tenant isolation is intrinsic:
/// the target <c>BroadcasterId</c> is stamped on every appended row regardless of what the source file carried,
/// so an export from one channel can never inject events into another.
/// </summary>
public sealed class EventJournalPortabilityService : IEventJournalPortabilityService
{
    // The forward-read page size for export and the journal head probe for import dedup counting.
    private const int BatchSize = 500;

    private readonly IEventJournal _journal;
    private readonly IEventUpcasterRegistry _upcasters;

    public EventJournalPortabilityService(IEventJournal journal, IEventUpcasterRegistry upcasters)
    {
        _journal = journal;
        _upcasters = upcasters;
    }

    public async Task<Result<long>> ExportAsync(
        Guid broadcasterId,
        System.IO.Stream destination,
        CancellationToken cancellationToken = default
    )
    {
        await using StreamWriter writer = new(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1 << 16,
            leaveOpen: true
        );

        long written = 0;
        long afterPosition = 0;
        while (true)
        {
            Result<IReadOnlyList<EventRecord>> batch = await _journal.ReadStreamAsync(
                broadcasterId,
                afterPosition,
                BatchSize,
                cancellationToken
            );
            if (batch.IsFailure)
                return Result.Failure<long>(
                    batch.ErrorMessage!,
                    batch.ErrorCode,
                    batch.ErrorDetail
                );

            if (batch.Value.Count == 0)
                break;

            foreach (EventRecord record in batch.Value)
            {
                string line = JsonConvert.SerializeObject(ToLine(record), Formatting.None);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                written++;
                afterPosition = record.StreamPosition;
            }

            if (batch.Value.Count < BatchSize)
                break;
        }

        await writer.FlushAsync(cancellationToken);
        return Result.Success(written);
    }

    public async Task<Result<EventJournalImportSummary>> ImportAsync(
        Guid targetBroadcasterId,
        System.IO.Stream source,
        CancellationToken cancellationToken = default
    )
    {
        Result<ParsedImport> parsed = await ParseAndPrepareAsync(
            targetBroadcasterId,
            source,
            cancellationToken
        );
        if (parsed.IsFailure)
            return Result.Failure<EventJournalImportSummary>(
                parsed.ErrorMessage!,
                parsed.ErrorCode,
                parsed.ErrorDetail
            );

        ParsedImport prepared = parsed.Value;

        // AppendBatchAsync runs the whole batch in one IUnitOfWork transaction (all-or-nothing) and is idempotent
        // on EventId — duplicates resolve to the existing row without consuming a new StreamPosition. The skip
        // count is computed up front against the live journal (events already present before this import).
        Result<IReadOnlyList<EventRecord>> appended = await _journal.AppendBatchAsync(
            prepared.Requests,
            cancellationToken
        );
        if (appended.IsFailure)
            return Result.Failure<EventJournalImportSummary>(
                appended.ErrorMessage!,
                appended.ErrorCode,
                appended.ErrorDetail
            );

        long imported = prepared.Requests.Count - prepared.SkippedDuplicate;
        return Result.Success(
            new EventJournalImportSummary(
                TotalLines: prepared.TotalLines,
                Imported: imported,
                SkippedDuplicate: prepared.SkippedDuplicate,
                Upcast: prepared.Upcast
            )
        );
    }

    // Reads the file line by line, upcasts each payload to current, re-tenants to the target, and counts the
    // events that already exist in the target stream (skips). Returns the prepared append requests. A malformed
    // line or a broken upcaster chain fails the whole import before any write happens.
    private async Task<Result<ParsedImport>> ParseAndPrepareAsync(
        Guid targetBroadcasterId,
        System.IO.Stream source,
        CancellationToken cancellationToken
    )
    {
        using StreamReader reader = new(
            source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1 << 16,
            leaveOpen: true
        );

        List<AppendEventRequest> requests = [];
        long totalLines = 0;
        long upcastCount = 0;
        long skipped = 0;
        long lineNumber = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            totalLines++;

            EventJournalExportLine? line;
            try
            {
                line = JsonConvert.DeserializeObject<EventJournalExportLine>(rawLine);
            }
            catch (JsonException ex)
            {
                return Result.Failure<ParsedImport>(
                    $"Import failed: line {lineNumber} is not valid JSON.",
                    "IMPORT_MALFORMED_LINE",
                    ex.Message
                );
            }

            if (line is null)
                return Result.Failure<ParsedImport>(
                    $"Import failed: line {lineNumber} deserialized to null.",
                    "IMPORT_MALFORMED_LINE"
                );

            if (line.EventId == Guid.Empty || string.IsNullOrWhiteSpace(line.EventType))
                return Result.Failure<ParsedImport>(
                    $"Import failed: line {lineNumber} is missing a required EventId or EventType.",
                    "IMPORT_INVALID_ENVELOPE"
                );

            // Bring the stored payload to the event type's current shape on the way in, so the importing
            // deployment's projections only ever fold the current version — even from an old export.
            Result<UpcastResult> upcast = _upcasters.UpcastToCurrent(
                line.EventType,
                line.EventVersion,
                line.PayloadJson
            );
            if (upcast.IsFailure)
                return Result.Failure<ParsedImport>(
                    $"Import failed: line {lineNumber} could not be upcast — {upcast.ErrorMessage}",
                    upcast.ErrorCode ?? "IMPORT_UPCAST_FAILED"
                );

            if (upcast.Value.Changed)
                upcastCount++;

            // A re-import of an event already in the target stream is a no-op (idempotency). Count it as a skip
            // up front; AppendBatchAsync will likewise consume no new position for it.
            Result<EventRecord> existing = await _journal.GetByEventIdAsync(
                line.EventId,
                cancellationToken
            );
            if (existing.IsSuccess)
                skipped++;

            // Re-tenant to the target: the source file's BroadcasterId/StreamPosition/Id are advisory; the import
            // stamps the caller's tenant and lets the journal allocate a fresh position. This is the isolation wall.
            requests.Add(
                new AppendEventRequest(
                    EventId: line.EventId,
                    BroadcasterId: targetBroadcasterId,
                    EventType: line.EventType,
                    EventVersion: upcast.Value.ToVersion,
                    Source: "import",
                    PayloadJson: upcast.Value.PayloadJson,
                    MetadataJson: line.MetadataJson,
                    OccurredAt: DateTime.SpecifyKind(line.OccurredAt, DateTimeKind.Utc),
                    CorrelationId: line.CorrelationId,
                    CausationId: line.CausationId,
                    ActorUserId: line.ActorUserId,
                    ActorTwitchUserId: line.ActorTwitchUserId
                )
            );
        }

        return Result.Success(new ParsedImport(requests, totalLines, upcastCount, skipped));
    }

    private static EventJournalExportLine ToLine(EventRecord record) =>
        new(
            record.Id,
            record.EventId,
            record.BroadcasterId,
            record.StreamPosition,
            record.EventType,
            record.EventVersion,
            record.Source,
            record.PayloadJson,
            record.MetadataJson,
            record.CorrelationId,
            record.CausationId,
            record.ActorUserId,
            record.ActorTwitchUserId,
            record.OccurredAt,
            record.RecordedAt
        );

    private sealed record ParsedImport(
        IReadOnlyList<AppendEventRequest> Requests,
        long TotalLines,
        long Upcast,
        long SkippedDuplicate
    );
}
