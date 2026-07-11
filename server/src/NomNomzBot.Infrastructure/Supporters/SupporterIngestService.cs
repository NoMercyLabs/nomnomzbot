// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Supporters.Events;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// The single supporter ingest path (supporter-events.md §3). Resolves the provider adapter, enforces that the
/// connection is enabled (default-deny), normalizes, dedups on <c>(BroadcasterId, SourceKey,
/// ProviderTransactionId)</c>, persists one <see cref="SupporterEvent"/>, and publishes exactly one
/// <see cref="SupporterEventReceived"/>. Disabled/absent connection or a duplicate = a no-op success; only an
/// unknown source or an unparseable payload fails.
/// </summary>
public sealed class SupporterIngestService : ISupporterIngestService
{
    private readonly IApplicationDbContext _db;
    private readonly IReadOnlyDictionary<string, ISupporterSource> _sources;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupporterIngestService> _logger;

    public SupporterIngestService(
        IApplicationDbContext db,
        IEnumerable<ISupporterSource> sources,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SupporterIngestService> logger
    )
    {
        _db = db;
        _sources = sources.ToDictionary(s => s.SourceKey, StringComparer.OrdinalIgnoreCase);
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> IngestAsync(
        Guid broadcasterId,
        string sourceKey,
        string rawPayload,
        CancellationToken ct = default
    )
    {
        if (!_sources.TryGetValue(sourceKey, out ISupporterSource? source))
            return Result.Failure($"Unknown supporter source '{sourceKey}'.", "VALIDATION_FAILED");

        SupporterConnection? connection = await _db.SupporterConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.SourceKey == sourceKey,
            ct
        );
        if (connection is null || !connection.IsEnabled)
        {
            // Default-deny: an unconfigured or disabled source silently ingests nothing.
            _logger.LogDebug(
                "Supporter ingest skipped for {Source} on {Channel}: no enabled connection.",
                sourceKey,
                broadcasterId
            );
            return Result.Success();
        }

        Result<SupporterEventDraft> normalized = await source.NormalizeAsync(rawPayload, ct);
        if (normalized.IsFailure)
            return Result.Failure(normalized.ErrorMessage!, normalized.ErrorCode!);

        SupporterEventDraft draft = normalized.Value;

        bool duplicate = await _db.SupporterEvents.AnyAsync(
            e =>
                e.BroadcasterId == broadcasterId
                && e.SourceKey == sourceKey
                && e.ProviderTransactionId == draft.ProviderTransactionId,
            ct
        );
        if (duplicate)
        {
            _logger.LogDebug(
                "Supporter ingest deduped {Source}/{Tx} on {Channel}.",
                sourceKey,
                draft.ProviderTransactionId,
                broadcasterId
            );
            return Result.Success();
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        SupporterEvent record = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            SourceKey = sourceKey,
            Kind = draft.Kind,
            SupporterDisplayName = draft.SupporterDisplayName,
            SupporterUserId = null, // Ko-fi identifies by name/email, not a platform id — no reliable viewer match.
            AmountMinor = draft.AmountMinor,
            Currency = draft.Currency,
            Tier = draft.Tier,
            Quantity = draft.Quantity,
            ItemsJson = draft.ItemsJson,
            MessageText = draft.MessageText,
            IsRecurring = draft.IsRecurring,
            ProviderTransactionId = draft.ProviderTransactionId,
            PayloadJson = draft.PayloadJson,
            ReceivedAt = now,
        };
        _db.SupporterEvents.Add(record);

        connection.LastEventAt = now;
        connection.Status = "active";
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new SupporterEventReceived
            {
                BroadcasterId = broadcasterId,
                SourceKey = sourceKey,
                Kind = record.Kind,
                SupporterDisplayName = record.SupporterDisplayName,
                SupporterUserId = record.SupporterUserId,
                AmountMinor = record.AmountMinor,
                Currency = record.Currency,
                Tier = record.Tier,
                Quantity = record.Quantity,
                MessageText = record.MessageText,
                IsRecurring = record.IsRecurring,
                SupporterEventId = record.Id,
            },
            ct
        );

        _logger.LogInformation(
            "Supporter {Kind} ingested from {Source} on {Channel} ({Tx}).",
            record.Kind,
            sourceKey,
            broadcasterId,
            record.ProviderTransactionId
        );
        return Result.Success();
    }
}
