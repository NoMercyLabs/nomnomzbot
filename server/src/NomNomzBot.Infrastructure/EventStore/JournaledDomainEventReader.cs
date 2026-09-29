// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Rebuilds the typed domain event a journal row stored: open the payload (a PII row is sealed under its
/// subject's key), upcast it to the current schema version, then deserialize it as the CLR type its
/// <c>EventType</c> names — the inverse of <c>EventStoreSubscriber</c>'s write. The type lookup is the closed
/// <see cref="DomainEventTypeRegistry"/>, never a type name taken from the row.
/// </summary>
public sealed class JournaledDomainEventReader
{
    private readonly IEventPayloadProtector _protector;
    private readonly IEventUpcasterRegistry _upcasters;
    private readonly DomainEventTypeRegistry _types;

    public JournaledDomainEventReader(
        IEventPayloadProtector protector,
        IEventUpcasterRegistry upcasters,
        DomainEventTypeRegistry types
    )
    {
        _protector = protector;
        _upcasters = upcasters;
        _types = types;
    }

    public async Task<Result<IDomainEvent>> ReadAsync(EventJournal row, CancellationToken ct)
    {
        Type? type = _types.Resolve(row.EventType);
        if (type is null)
            return Result.Failure<IDomainEvent>(
                $"'{row.EventType}' is not a domain event type.",
                "NOT_A_DOMAIN_EVENT"
            );

        EventRecord record = EventJournalService.Map(row);
        Result<string> payload = await _protector.UnprotectAsync(record, ct);
        if (payload.IsFailure)
            return payload.ToTyped<IDomainEvent>();

        Result<UpcastResult> upcast = _upcasters.UpcastToCurrent(
            record.EventType,
            record.EventVersion,
            payload.Value
        );
        if (upcast.IsFailure)
            return upcast.ToTyped<IDomainEvent>();

        try
        {
            return
                JsonConvert.DeserializeObject(upcast.Value.PayloadJson, type)
                    is IDomainEvent domainEvent
                ? Result.Success(domainEvent)
                : Result.Failure<IDomainEvent>("The journal payload is empty.", "PAYLOAD_INVALID");
        }
        catch (JsonException ex)
        {
            return Result.Failure<IDomainEvent>(ex.Message, "PAYLOAD_INVALID");
        }
    }
}
