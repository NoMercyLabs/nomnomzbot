// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// IEventBus implementation that resolves handlers from DI, executes them sequentially,
/// and isolates individual handler failures. Registered as a singleton.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly EventLogger _eventLogger;
    private readonly IChannelOnboardingGate _onboardingGate;

    public EventBus(
        IServiceProvider serviceProvider,
        ILogger<EventBus> logger,
        EventLogger eventLogger,
        IChannelOnboardingGate onboardingGate
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _eventLogger = eventLogger;
        _onboardingGate = onboardingGate;
    }

    /// <summary>
    /// The one choke point for "the bot stays silent in a channel that did not onboard": a platform-ingress
    /// event of such a channel reaches only <see cref="IOnboardingExemptHandler"/> handlers (they record, they
    /// never act). Every other event, and every onboarded or unknown channel, keeps its full handler set.
    /// </summary>
    private async Task<List<IEventHandler<TEvent>>> ResolveHandlersAsync<TEvent>(
        IServiceProvider services,
        TEvent @event,
        CancellationToken cancellationToken
    )
        where TEvent : class, IDomainEvent
    {
        List<IEventHandler<TEvent>> handlers = [.. services.GetServices<IEventHandler<TEvent>>()];
        if (
            handlers.Count == 0
            || @event is not DomainEventBase { IsChannelIngress: true }
            || !await _onboardingGate.IsClosedAsync(@event.BroadcasterId, cancellationToken)
        )
            return handlers;

        handlers.RemoveAll(h => h is not IOnboardingExemptHandler);
        _logger.LogDebug(
            "Channel {BroadcasterId} is not onboarded - {EventType} reaches only its recording handlers",
            @event.BroadcasterId,
            typeof(TEvent).Name
        );
        return handlers;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IDomainEvent
    {
        string eventType = typeof(TEvent).Name;
        _logger.LogDebug("Publishing event {EventType} ({EventId})", eventType, @event.EventId);

        _eventLogger.Log(@event);

        // Scope lives for the full duration of handler execution so that scoped
        // services (DbContext, IPipelineEngine, IChatProvider, …) remain valid.
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        List<IEventHandler<TEvent>> handlers = await ResolveHandlersAsync(
            scope.ServiceProvider,
            @event,
            cancellationToken
        );

        if (handlers.Count == 0)
        {
            _logger.LogTrace("No handlers registered for {EventType}", eventType);
            return;
        }

        // Execute handlers sequentially — all share a single scoped DbContext which is NOT
        // thread-safe. Running them in parallel via Task.WhenAll causes "A second operation
        // was started on this context instance" when two handlers hit EF Core async queries
        // concurrently on the same context instance.
        foreach (IEventHandler<TEvent> handler in handlers)
            await ExecuteHandler(handler, @event, cancellationToken);
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent
    {
        string eventType = typeof(TEvent).Name;
        _logger.LogDebug(
            "Publishing fire-and-forget event {EventType} ({EventId})",
            eventType,
            @event.EventId
        );

        _eventLogger.Log(@event);

        // Use Task.Run to ensure execution happens on the thread pool,
        // completely detached from the caller's context.
        _ = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
            List<IEventHandler<TEvent>> handlers = await ResolveHandlersAsync(
                scope.ServiceProvider,
                @event,
                CancellationToken.None
            );

            if (handlers.Count == 0)
                return;

            foreach (IEventHandler<TEvent> handler in handlers)
                await ExecuteHandler(handler, @event, CancellationToken.None);
        });
    }

    private async Task ExecuteHandler<TEvent>(
        IEventHandler<TEvent> handler,
        TEvent @event,
        CancellationToken cancellationToken
    )
        where TEvent : class, IDomainEvent
    {
        string handlerName = handler.GetType().Name;
        try
        {
            _logger.LogTrace(
                "Executing handler {Handler} for {EventType}",
                handlerName,
                typeof(TEvent).Name
            );

            await handler.HandleAsync(@event, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Handler {Handler} cancelled for {EventType}",
                handlerName,
                typeof(TEvent).Name
            );
        }
        catch (Exception ex)
        {
            // Critical: one handler's failure must NOT affect other handlers
            _logger.LogError(
                ex,
                "Handler {Handler} failed for event {EventType} ({EventId})",
                handlerName,
                typeof(TEvent).Name,
                @event.EventId
            );
        }
    }
}
