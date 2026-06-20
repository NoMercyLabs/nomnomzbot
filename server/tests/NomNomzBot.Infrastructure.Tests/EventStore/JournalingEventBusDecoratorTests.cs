// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Behavior tests for the journaling decorator: every publish is captured to the journal, the post-commit hook
/// fires for the committed row (and only after a successful commit, never when capture fails), and the wrapped
/// bus still delivers the event. Asserts the journaled row, the hook's recorded row, and inner delegation.
/// </summary>
public sealed class JournalingEventBusDecoratorTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private sealed class CapturableEvent : DomainEventBase
    {
        public string Note { get; init; } = string.Empty;
    }

    /// <summary>Records every event delegated to it so the test can assert the wrapped bus still ran.</summary>
    private sealed class RecordingInnerBus : IEventBus
    {
        public List<IDomainEvent> Delivered { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            Delivered.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Delivered.Add(@event);
    }

    // A provider whose scope yields the real journal-backed subscriber over a shared SQLite db, plus the hook.
    private static ServiceProvider BuildProvider(
        SqliteTestDatabase database,
        RecordingPostCommitHook hook
    )
    {
        ServiceCollection services = new();
        services.AddScoped<IApplicationDbContext>(_ => database.NewContext());
        services.AddScoped<IUnitOfWork>(sp => new EventStoreTestUnitOfWork(
            (EventStoreTestDbContext)sp.GetRequiredService<IApplicationDbContext>()
        ));
        services.AddScoped<ITenantSequenceAllocator>(sp => new TenantSequenceAllocator(
            sp.GetRequiredService<IApplicationDbContext>()
        ));
        services.AddSingleton<IEventUpcasterRegistry>(new EventUpcasterRegistry([]));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<IEventJournal>(sp => new EventJournalService(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp.GetRequiredService<ITenantSequenceAllocator>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<TimeProvider>()
        ));
        services.AddScoped<IEventStoreSubscriber>(sp => new EventStoreSubscriber(
            sp.GetRequiredService<IEventJournal>(),
            sp.GetRequiredService<IEventUpcasterRegistry>()
        ));
        services.AddScoped<IJournalPostCommitHook>(_ => hook);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Publish_CapturesToJournal_FiresHook_AndDelegatesToInnerBus()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        RecordingPostCommitHook hook = new();
        RecordingInnerBus inner = new();

        await using ServiceProvider provider = BuildProvider(database, hook);
        JournalingEventBusDecorator decorator = new(
            inner,
            provider,
            NullLogger<JournalingEventBusDecorator>.Instance
        );

        Guid tenant = Guid.NewGuid();
        CapturableEvent @event = new() { BroadcasterId = tenant, Note = "hello" };

        await decorator.PublishAsync(@event);

        // 1. The hook fired for exactly the committed row (after a successful append).
        hook.Committed.Should().ContainSingle();
        EventRecord committed = hook.Committed.Single();
        committed.EventId.Should().Be(Guid.Parse(@event.EventId));
        committed.EventType.Should().Be(nameof(CapturableEvent));
        committed.BroadcasterId.Should().Be(tenant);
        committed.StreamPosition.Should().Be(1);
        committed.Source.Should().Be("domain");

        // 2. The row is actually durable in the journal.
        await using EventStoreTestDbContext verify = database.NewContext();
        EventJournalService journal = new(
            verify,
            new TenantSequenceAllocator(verify),
            new EventStoreTestUnitOfWork(verify),
            Clock
        );
        Result<EventRecord> stored = await journal.GetByEventIdAsync(Guid.Parse(@event.EventId));
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Note().Should().Be("hello");

        // 3. The wrapped bus still delivered the event to live handlers.
        inner.Delivered.Should().ContainSingle().Which.Should().BeSameAs(@event);
    }

    [Fact]
    public async Task Publish_WhenCaptureFails_HookDoesNotFire_ButInnerBusStillDelivers()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        RecordingPostCommitHook hook = new();
        RecordingInnerBus inner = new();

        await using ServiceProvider provider = BuildProvider(database, hook);
        JournalingEventBusDecorator decorator = new(
            inner,
            provider,
            NullLogger<JournalingEventBusDecorator>.Instance
        );

        // A bad EventId makes CaptureAsync fail → there is no committed row → no post-commit hook may fire.
        CapturableEvent @event = new() { EventId = "not-a-guid", BroadcasterId = Guid.NewGuid() };

        await decorator.PublishAsync(@event);

        hook.Committed.Should().BeEmpty("a hook must not fire when no journal row committed");
        inner
            .Delivered.Should()
            .ContainSingle(
                "the journal is best-effort: live delivery proceeds even if capture failed"
            );
    }
}

file static class CapturableEventRecordExtensions
{
    // Reads the serialized "Note" back out of the journaled payload, proving the event body round-tripped.
    public static string? Note(this EventRecord record) =>
        Newtonsoft.Json.Linq.JObject.Parse(record.PayloadJson).Value<string>("Note");
}
