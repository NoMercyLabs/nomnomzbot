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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Domain.Quotes.Events;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Quotes;

/// <summary>
/// Behavior tests for the quote library. Each proves a consequence of an action — the row that lands, the
/// number assigned, the never-reused invariant, the event emitted — not merely that a call returned non-null.
/// Runs on real SQLite so the unique <c>(BroadcasterId, Number)</c> constraint and the add-under-transaction
/// path are genuinely exercised.
/// </summary>
public sealed class QuoteServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static QuoteService NewService(QuoteTestDbContext db, RecordingEventBus bus)
    {
        QuoteTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new QuoteService(db, allocator, uow, bus, Clock);
    }

    private static async Task<Guid> SeedChannelAsync(QuoteSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using QuoteTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    [Fact]
    public async Task AddAsync_AssignsNumberOne_PersistsFullShape_AndPublishesEvent()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid author = Guid.CreateVersion7();
        RecordingEventBus bus = new();

        QuoteDto created;
        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            Result<QuoteDto> result = await service.AddAsync(
                channel,
                new AddQuoteRequest("blame the lag", "Stoney_Eagle", "Just Chatting", null, author)
            );

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            created = result.Value;
        }

        // The first quote on a channel is #1.
        created.Number.Should().Be(1);

        // The persisted row carries the full shape, not just an id.
        await using (QuoteTestDbContext db = database.NewContext())
        {
            Quote stored = await db.Quotes.SingleAsync(q => q.BroadcasterId == channel);
            stored.Id.Should().Be(created.Id);
            stored.Number.Should().Be(1);
            stored.Text.Should().Be("blame the lag");
            stored.QuotedDisplayName.Should().Be("Stoney_Eagle");
            stored.ContextGame.Should().Be("Just Chatting");
            stored.CreatedByUserId.Should().Be(author);
            // QuotedAt defaults to creation time when not supplied (schema G.5).
            stored.QuotedAt.Should().Be(Clock.GetUtcNow().UtcDateTime);
        }

        // The QuoteAddedEvent fired carrying the assigned number + author + tenant.
        QuoteAddedEvent published = bus.Published.OfType<QuoteAddedEvent>().Single();
        published.QuoteId.Should().Be(created.Id);
        published.Number.Should().Be(1);
        published.CreatedByUserId.Should().Be(author);
        published.BroadcasterId.Should().Be(channel);
    }

    [Fact]
    public async Task AddAsync_AllocatesSequentialNumbers_PerChannel()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        List<int> numbers = [];
        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            foreach (string text in new[] { "one", "two", "three" })
            {
                Result<QuoteDto> result = await service.AddAsync(
                    channel,
                    new AddQuoteRequest(text, null, null, null, null)
                );
                result.IsSuccess.Should().BeTrue(result.ErrorMessage);
                numbers.Add(result.Value.Number);
            }
        }

        numbers
            .Should()
            .Equal(new[] { 1, 2, 3 }, "numbers are per-channel monotonic starting at 1 (D1)");
    }

    [Fact]
    public async Task DeleteThenAdd_NeverReusesADeletedNumber()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            await service.AddAsync(channel, new AddQuoteRequest("one", null, null, null, null));
            await service.AddAsync(channel, new AddQuoteRequest("two", null, null, null, null));
            await service.AddAsync(channel, new AddQuoteRequest("three", null, null, null, null));
        }

        // Delete #2 (soft-delete; the row + its number are retained).
        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            Result deleted = await service.DeleteAsync(channel, 2);
            deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        }

        int newNumber;
        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            Result<QuoteDto> added = await service.AddAsync(
                channel,
                new AddQuoteRequest("four", null, null, null, null)
            );
            added.IsSuccess.Should().BeTrue(added.ErrorMessage);
            newNumber = added.Value.Number;
        }

        // The next quote is #4, NOT a reused #2 — the deleted number is gone forever (D1).
        newNumber.Should().Be(4);

        await using (QuoteTestDbContext db = database.NewContext())
        {
            // #2 is not retrievable (soft-deleted) and was never handed back out.
            QuoteService service = NewService(db, bus);
            Result<QuoteDto> gone = await service.GetAsync(channel, 2);
            gone.IsFailure.Should().BeTrue();
            gone.ErrorCode.Should().Be("NOT_FOUND");
        }
    }

    [Fact]
    public async Task GetRandomAsync_ReturnsOnlyNonDeletedQuotes()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            await service.AddAsync(channel, new AddQuoteRequest("keep", null, null, null, null));
            await service.AddAsync(channel, new AddQuoteRequest("delete", null, null, null, null));
            await service.DeleteAsync(channel, 2);
        }

        // 25 draws should only ever surface the surviving quote — the deleted one is excluded by the filter.
        await using QuoteTestDbContext readDb = database.NewContext();
        QuoteService reader = NewService(readDb, bus);
        for (int i = 0; i < 25; i++)
        {
            Result<QuoteDto> result = await reader.GetRandomAsync(channel);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.Number.Should().Be(1);
            result.Value.Text.Should().Be("keep");
        }
    }

    [Fact]
    public async Task GetRandomAsync_FailsQuotesEmpty_OnEmptyChannel()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using QuoteTestDbContext db = database.NewContext();
        QuoteService service = NewService(db, bus);

        Result<QuoteDto> result = await service.GetRandomAsync(channel);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("QUOTES_EMPTY");
    }

    [Fact]
    public async Task EditAsync_ChangesTextAndAttribution_ButNotTheNumber()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            await service.AddAsync(
                channel,
                new AddQuoteRequest("original", "Alice", "Game A", null, null)
            );
        }

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            Result<QuoteDto> edited = await service.EditAsync(
                channel,
                1,
                new EditQuoteRequest("edited", "Bob", "Game B")
            );
            edited.IsSuccess.Should().BeTrue(edited.ErrorMessage);
            edited.Value.Number.Should().Be(1, "the number is immutable across an edit");
        }

        await using (QuoteTestDbContext db = database.NewContext())
        {
            Quote stored = await db.Quotes.SingleAsync(q => q.BroadcasterId == channel);
            stored.Number.Should().Be(1);
            stored.Text.Should().Be("edited");
            stored.QuotedDisplayName.Should().Be("Bob");
            stored.ContextGame.Should().Be("Game B");
        }
    }

    [Fact]
    public async Task ListAsync_FiltersByTerm_OverTextAndAttribution()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            await service.AddAsync(
                channel,
                new AddQuoteRequest("the lag is real", "Stoney_Eagle", null, null, null)
            );
            await service.AddAsync(
                channel,
                new AddQuoteRequest("gg well played", "OtherUser", null, null, null)
            );
        }

        await using QuoteTestDbContext readDb = database.NewContext();
        QuoteService reader = NewService(readDb, bus);

        // Term matches the body of #1 only.
        Result<PagedList<QuoteDto>> byText = await reader.ListAsync(
            channel,
            new QuoteSearch("lag"),
            new PaginationParams(1, 25)
        );
        byText.IsSuccess.Should().BeTrue();
        byText.Value.Items.Should().ContainSingle().Which.Number.Should().Be(1);

        // Term matches the attribution of #2 only.
        Result<PagedList<QuoteDto>> byName = await reader.ListAsync(
            channel,
            new QuoteSearch("OtherUser"),
            new PaginationParams(1, 25)
        );
        byName.IsSuccess.Should().BeTrue();
        byName.Value.Items.Should().ContainSingle().Which.Number.Should().Be(2);
    }

    [Fact]
    public async Task Quotes_AreScopedPerChannel()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channelA = await SeedChannelAsync(database);
        Guid channelB = await SeedChannelAsync(database);
        RecordingEventBus bus = new();

        await using (QuoteTestDbContext db = database.NewContext())
        {
            QuoteService service = NewService(db, bus);
            await service.AddAsync(channelA, new AddQuoteRequest("a-one", null, null, null, null));
            await service.AddAsync(channelB, new AddQuoteRequest("b-one", null, null, null, null));
        }

        await using QuoteTestDbContext readDb = database.NewContext();
        QuoteService reader = NewService(readDb, bus);

        // Each channel restarts numbering at #1 and only sees its own quote.
        Result<QuoteDto> a = await reader.GetAsync(channelA, 1);
        Result<QuoteDto> b = await reader.GetAsync(channelB, 1);
        a.Value.Text.Should().Be("a-one");
        b.Value.Text.Should().Be("b-one");

        Result<PagedList<QuoteDto>> listA = await reader.ListAsync(
            channelA,
            new QuoteSearch(null),
            new PaginationParams(1, 25)
        );
        listA.Value.Items.Should().ContainSingle().Which.Text.Should().Be("a-one");
    }
}
