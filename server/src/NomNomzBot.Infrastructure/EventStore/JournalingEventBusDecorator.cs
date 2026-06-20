// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Makes the event store a durable subscriber to the bus. On every publish it FIRST captures the event to the
/// journal (in a fresh scope, mirroring how <c>EventBus</c> resolves scoped handlers), THEN invokes every
/// registered <see cref="IJournalPostCommitHook"/> for the committed row (failures isolated and logged — a
/// faulting hook never blocks the commit or delegation), and FINALLY delegates to the wrapped bus so live
/// handlers still run. This is the single place that sees every event, so it is the only post-commit seam.
/// <para>
/// Capture failures do not throw: the journal is best-effort relative to live delivery (the event still reaches
/// handlers). When capture fails there is no committed row, so no post-commit hook runs.
/// </para>
/// </summary>
public sealed class JournalingEventBusDecorator : IEventBus
{
    private readonly IEventBus _inner;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JournalingEventBusDecorator> _logger;

    public JournalingEventBusDecorator(
        IEventBus inner,
        IServiceProvider serviceProvider,
        ILogger<JournalingEventBusDecorator> logger
    )
    {
        _inner = inner;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IDomainEvent
    {
        await CaptureAndFireHooksAsync(@event, cancellationToken);
        await _inner.PublishAsync(@event, cancellationToken);
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent
    {
        _ = Task.Run(async () =>
        {
            await CaptureAndFireHooksAsync(@event, CancellationToken.None);
        });
        _inner.PublishFireAndForget(@event);
    }

    private async Task CaptureAndFireHooksAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken
    )
        where TEvent : class, IDomainEvent
    {
        // A scope so the scoped store services (DbContext-backed subscriber, hooks) resolve correctly, exactly
        // as EventBus opens a scope to resolve scoped handlers.
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        EventRecord committed;
        try
        {
            IEventStoreSubscriber subscriber =
                scope.ServiceProvider.GetRequiredService<IEventStoreSubscriber>();
            Result<EventRecord> capture = await subscriber.CaptureAsync(@event, cancellationToken);
            if (capture.IsFailure)
            {
                _logger.LogError(
                    "Journal capture failed for {EventType} ({EventId}): {Error}",
                    typeof(TEvent).Name,
                    @event.EventId,
                    capture.ErrorMessage
                );
                return;
            }

            committed = capture.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Journal capture threw for {EventType} ({EventId})",
                typeof(TEvent).Name,
                @event.EventId
            );
            return;
        }

        await InvokeHooksAsync(scope.ServiceProvider, committed, cancellationToken);
    }

    private async Task InvokeHooksAsync(
        IServiceProvider scopedProvider,
        EventRecord committed,
        CancellationToken cancellationToken
    )
    {
        IEnumerable<IJournalPostCommitHook> hooks =
            scopedProvider.GetServices<IJournalPostCommitHook>();

        foreach (IJournalPostCommitHook hook in hooks)
        {
            try
            {
                Result result = await hook.OnCommittedAsync(committed, cancellationToken);
                if (result.IsFailure)
                    _logger.LogError(
                        "Post-commit hook {Hook} failed for {EventId}: {Error}",
                        hook.GetType().Name,
                        committed.EventId,
                        result.ErrorMessage
                    );
            }
            catch (Exception ex)
            {
                // Isolation: a faulting hook never rolls back the commit or blocks the others / delegation.
                _logger.LogError(
                    ex,
                    "Post-commit hook {Hook} threw for {EventId}",
                    hook.GetType().Name,
                    committed.EventId
                );
            }
        }
    }
}
