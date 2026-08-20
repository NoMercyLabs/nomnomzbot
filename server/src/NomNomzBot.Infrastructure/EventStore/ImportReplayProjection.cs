// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// "Replay" (event-store §1.1): re-executes every side effect an IMPORTED event's original bot would have run
/// — pipeline triggers, TTS-enabled commands, reward handling — WITHOUT re-firing anything that has already
/// happened live on this deployment. Modeled as an ordinary <see cref="IProjection"/> so it inherits the
/// runner's existing per-tenant checkpoint, ordering, and fault handling for free: driven forward via
/// <c>IProjectionRunner.RunOnceAsync</c>, it only ever advances past events already in THIS tenant's own
/// journal stream (<c>ReadStreamAsync</c> is tenant-scoped by construction — a caller cannot make it read
/// another broadcaster's rows), so there is no cross-tenant replay surface: nothing "from someone else" can
/// ever reach <see cref="ApplyAsync"/> for a channel that didn't journal it.
/// <para>
/// Only <c>Source == "import"</c> rows are ever republished — ordinary live ("eventsub"/"domain") rows are
/// skipped (still counted as applied, so the checkpoint advances past them). This is what keeps Replay from
/// re-triggering a channel's normal, already-handled activity: it only ever resurrects events that arrived
/// through a portable import, and running it again after the checkpoint has caught up is a safe no-op.
/// </para>
/// <para>
/// Republishing routes through the ordinary <see cref="IEventBus"/> — the same path <c>JournalingEventBusDecorator</c>
/// uses for every live event — so it reaches every real handler (pipeline triggers, TTS dispatch, currency
/// awards, chat announcements) exactly as it would have on the original bot. The decorator's own
/// idempotent-by-EventId capture means this can never create a second journal row for the same event.
/// </para>
/// </summary>
public sealed class ImportReplayProjection : IProjection
{
    private static readonly MethodInfo PublishMethod = typeof(IEventBus).GetMethod(
        nameof(IEventBus.PublishAsync)
    )!;

    private readonly IEventBus _eventBus;
    private readonly DomainEventTypeRegistry _types;
    private readonly ILogger<ImportReplayProjection> _logger;

    public string Name => "import-replay";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes { get; } = new HashSet<string>();

    public ImportReplayProjection(
        IEventBus eventBus,
        DomainEventTypeRegistry types,
        ILogger<ImportReplayProjection> logger
    )
    {
        _eventBus = eventBus;
        _types = types;
        _logger = logger;
    }

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.Source != "import")
            return Result.Success(); // not an imported event — nothing to replay, checkpoint still advances

        Type? clrType = _types.Resolve(@event.EventType);
        if (clrType is null)
        {
            _logger.LogWarning(
                "Import replay: unknown EventType '{EventType}' for imported event {EventId} — skipped, not a known domain event",
                @event.EventType,
                @event.EventId
            );
            return Result.Success(); // unknown type — skip rather than fail the whole catch-up run
        }

        object? domainEvent;
        try
        {
            domainEvent = JsonConvert.DeserializeObject(@event.PayloadJson, clrType);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Import replay: could not deserialize imported event {EventId} ({EventType}) — skipped",
                @event.EventId,
                @event.EventType
            );
            return Result.Success();
        }

        if (domainEvent is not DomainEventBase typed)
            return Result.Success();

        // The stored payload already carries the correct (re-tenanted-on-import) BroadcasterId, EventId, and
        // OccurredAt from the journal row itself — deserializing it back onto the same concrete type preserves
        // all three, so the republish is byte-for-byte the same tenant/identity/time the import committed.
        MethodInfo generic = PublishMethod.MakeGenericMethod(clrType);
        await (Task)generic.Invoke(_eventBus, [domainEvent, cancellationToken])!;

        return Result.Success();
    }

    public Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success()); // no derived table of our own — nothing to clear
}
