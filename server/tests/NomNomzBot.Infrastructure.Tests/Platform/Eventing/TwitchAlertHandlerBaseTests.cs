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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The alert handler logs one ChannelEvent per event, keyed by the domain event's <c>EventId</c> — the SAME id
/// <c>TwitchChannelEventLogProjection</c> uses — so the instant handler write and the later projection enrichment
/// collapse into ONE row. A fresh id per write made every alert event show up TWICE in the activity feed. Also
/// proves the write is idempotent when the event is re-delivered or the projection already wrote the row.
/// </summary>
public sealed class TwitchAlertHandlerBaseTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000abc0e");

    private sealed record TestFollow(Guid EventId, Guid BroadcasterId) : IDomainEvent
    {
        public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
    }

    // Exposes the base's protected LogChannelEventAsync so the id/idempotency behavior can be asserted directly.
    private sealed class TestAlertHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline
    ) : TwitchAlertHandlerBase<TestFollow>(scopeFactory, pipeline, NullLogger.Instance)
    {
        protected override string EventTypeKey => "channel.follow";

        protected override string? GetUserId(TestFollow @event) => "906093391";

        protected override string? GetUserDisplayName(TestFollow @event) => "MrFunnyGoodFeeling";

        protected override Dictionary<string, string> BuildVariables(TestFollow @event) =>
            new() { ["user"] = "MrFunnyGoodFeeling" };

        public Task Log(IApplicationDbContext db, TestFollow @event) =>
            LogChannelEventAsync(db, @event, @event.BroadcasterId, CancellationToken.None);
    }

    private static TestAlertHandler NewHandler() =>
        new(Substitute.For<IServiceScopeFactory>(), Substitute.For<IPipelineEngine>());

    [Fact]
    public async Task Logs_the_channel_event_keyed_by_the_domain_EventId()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();
        TestFollow @event = new(Guid.CreateVersion7(), Channel);

        await NewHandler().Log(db, @event);

        ChannelEvent row = await db.ChannelEvents.AsNoTracking().SingleAsync();
        row.Id.Should().Be(@event.EventId.ToString()); // the projection's key — NOT a fresh ULID
        row.Type.Should().Be("channel.follow");
        row.ChannelId.Should().Be(Channel);
    }

    [Fact]
    public async Task Re_delivering_the_same_event_does_not_duplicate_the_row()
    {
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();
        TestFollow @event = new(Guid.CreateVersion7(), Channel);

        await NewHandler().Log(db, @event);
        await NewHandler().Log(db, @event); // EventSub re-delivery / projection race

        (await db.ChannelEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_existing_row_for_the_EventId_is_left_intact()
    {
        // Simulates the projection having written the row first (same EventId key): the handler must not
        // add a second row nor overwrite the existing one.
        using ReadModelRebuildDatabase database = ReadModelRebuildDatabase.Open();
        await using ReadModelRebuildDbContext db = database.NewContext();
        Guid eventId = Guid.CreateVersion7();
        db.ChannelEvents.Add(
            new ChannelEvent
            {
                Id = eventId.ToString(),
                ChannelId = Channel,
                Type = "channel.follow",
                Data = "{\"pre\":\"existing\"}",
            }
        );
        await db.SaveChangesAsync();

        await NewHandler().Log(db, new TestFollow(eventId, Channel));

        ChannelEvent row = await db.ChannelEvents.AsNoTracking().SingleAsync();
        row.Data.Should().Contain("existing"); // handler skipped — no overwrite, no duplicate
    }
}
