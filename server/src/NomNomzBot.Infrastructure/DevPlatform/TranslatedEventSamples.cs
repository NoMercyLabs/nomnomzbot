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
using System.Runtime.CompilerServices;
using System.Text.Json;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Turns each real EventSub wire fixture (<see cref="EventSamplePayloads.ByWireName"/>) into the domain event it
/// becomes, by running the fixture through the real translator that owns it. A script never receives the wire
/// payload; it receives the domain event. So the catalog sample must be that event, written with the schema's
/// own names (<see cref="DomainEventSampleWriter"/>). The samples keep the fixture's real values and need no hand
/// upkeep: a new translator or a new fixture is picked up on the next first use.
/// <para>
/// Translators are found by reflection and built with a capturing bus, a fixed clock and a
/// <see cref="NoStateStandIn"/> for any other interface dependency. A fixture no translator turns into its event
/// has no entry here, and the catalog falls back to <see cref="ReflectionSampleGenerator"/> for it.
/// </para>
/// </summary>
internal sealed class TranslatedEventSamples
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid FixedTenant = new("11111111-2222-3333-4444-555555555555");

    // The emitter is scoped, the catalog a singleton: caching per catalog translates once per process, lazily,
    // on the first catalog request.
    private static readonly ConditionalWeakTable<IEventCatalog, TranslatedEventSamples> Cache = [];

    private readonly IReadOnlyDictionary<Type, IDomainEvent> _byEventType;

    private TranslatedEventSamples(IEventCatalog catalog) =>
        _byEventType = Translate(catalog.Descriptors);

    /// <summary>The translated samples of <paramref name="catalog"/>, built on first use and then cached.</summary>
    public static TranslatedEventSamples For(IEventCatalog catalog) =>
        Cache.GetValue(catalog, c => new TranslatedEventSamples(c));

    /// <summary>The event types whose sample comes from a translator.</summary>
    public IReadOnlyCollection<Type> TranslatedTypes => [.. _byEventType.Keys];

    /// <summary>The translated sample for <paramref name="descriptor"/>, or null when no translator produced one.</summary>
    public string? SampleFor(EventDescriptor descriptor, SdkContext context) =>
        _byEventType.TryGetValue(descriptor.ClrType, out IDomainEvent? translated)
            ? DomainEventSampleWriter.Write(translated, context)
            : null;

    private static Dictionary<Type, IDomainEvent> Translate(
        IReadOnlyList<EventDescriptor> descriptors
    )
    {
        List<TranslatorHarness> harnesses = BuildHarnesses();
        Dictionary<Type, IDomainEvent> translated = [];

        foreach (KeyValuePair<string, string> fixture in EventSamplePayloads.ByWireName)
        {
            EventDescriptor? descriptor = descriptors.FirstOrDefault(d =>
                d.WireName == fixture.Key
            );
            if (descriptor is null)
                continue;

            foreach (TranslatorHarness harness in harnesses)
            {
                IDomainEvent? match = harness
                    .Run(fixture.Value)
                    .FirstOrDefault(e => e.GetType() == descriptor.ClrType);
                if (match is null)
                    continue;
                translated[descriptor.ClrType] = match;
                break;
            }
        }
        return translated;
    }

    private static List<TranslatorHarness> BuildHarnesses()
    {
        List<TranslatorHarness> harnesses = [];
        foreach (
            Type type in typeof(TranslatedEventSamples)
                .Assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsClass: true })
                .Where(t => t.IsAssignableTo(typeof(IEventSubEventTranslator)))
        )
        {
            CapturingEventBus bus = new();
            IEventSubEventTranslator? translator = TryConstruct(type, bus);
            if (translator is not null)
                harnesses.Add(new(translator, bus));
        }
        return harnesses;
    }

    private static IEventSubEventTranslator? TryConstruct(Type type, CapturingEventBus bus)
    {
        ConstructorInfo[] constructors = type.GetConstructors();
        if (constructors.Length != 1)
            return null;

        List<object> arguments = [];
        foreach (ParameterInfo parameter in constructors[0].GetParameters())
        {
            if (parameter.ParameterType == typeof(IEventBus))
                arguments.Add(bus);
            else if (parameter.ParameterType == typeof(TimeProvider))
                arguments.Add(new FixedTimeProvider());
            else if (parameter.ParameterType.IsInterface)
                arguments.Add(
                    DispatchProxy.Create(parameter.ParameterType, typeof(NoStateStandIn))
                );
            else
                return null;
        }
        return (IEventSubEventTranslator)constructors[0].Invoke([.. arguments]);
    }

    private sealed class TranslatorHarness(
        IEventSubEventTranslator translator,
        CapturingEventBus bus
    )
    {
        public IReadOnlyList<IDomainEvent> Run(string fixtureJson)
        {
            bus.Captured.Clear();
            using JsonDocument document = JsonDocument.Parse(fixtureJson);
            JsonElement payload = document.RootElement.Clone();
            EventSubNotification notification = new()
            {
                MessageId = "sample-notification",
                MessageTimestamp = FixedNow,
                SubscriptionType = translator.SubscriptionType,
                SubscriptionVersion = "1",
                BroadcasterId = FixedTenant,
                TwitchBroadcasterUserId =
                    payload.TryGetProperty("broadcaster_user_id", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                        ? id.GetString()!
                        : "broadcaster-99",
                Event = payload,
            };

            try
            {
                Task running = translator.TranslateAsync(notification);
                // Every dependency here completes synchronously, so a translator still running after the
                // call has nothing a sample can use.
                return running.IsCompletedSuccessfully ? [.. bus.Captured] : [];
            }
            catch (Exception)
            {
                // A translator handed another subscription type's fixture rejects it: the "not mine" answer.
                return [];
            }
        }
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<IDomainEvent> Captured { get; } = [];

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Captured.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Captured.Add(@event);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }
}
