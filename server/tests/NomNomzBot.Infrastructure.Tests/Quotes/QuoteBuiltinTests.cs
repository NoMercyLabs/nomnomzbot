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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Quotes.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Quotes;

/// <summary>
/// Behavior tests for the <c>!quote</c> built-in — the command that was MISSING (the bug: <c>!quote 1</c>
/// never replied). It must fetch the requested quote by number, post a random one with no argument, and — the
/// crux — ALWAYS reply (a success with a friendly message) even when the number doesn't exist or there are no
/// quotes, so the viewer never gets silence.
/// </summary>
public sealed class QuoteBuiltinTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static QuoteService NewQuoteService(QuoteTestDbContext db)
    {
        QuoteTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new QuoteService(db, allocator, uow, new RecordingEventBus(), Clock);
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

    private static BuiltinCommandContext Context(Guid broadcasterId, string args) =>
        new()
        {
            BroadcasterId = broadcasterId,
            TriggeringUserId = "999",
            TriggeringUserDisplayName = "viewer",
            Args = args,
        };

    [Fact]
    public async Task Quote_WithNumber_PostsThatQuote()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("first", null, null, null, null));
        await quotes.AddAsync(
            channel,
            new AddQuoteRequest("blame the lag", "Stoney_Eagle", "Just Chatting", null, null)
        );

        QuoteBuiltin builtin = new(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, "2"));

        // !quote 2 replies with quote #2 in the canonical form — the whole point that was broken.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#2: \"blame the lag\" — Stoney_Eagle (Just Chatting)");
    }

    [Fact]
    public async Task Quote_WithoutNumber_PostsARandomQuote()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("only one", null, null, null, null));

        QuoteBuiltin builtin = new(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, string.Empty));

        // With a single quote a random pick must be #1.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#1: \"only one\"");
    }

    [Fact]
    public async Task Quote_NumberNotFound_StillReplies_WithAFriendlyMessage()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("only one", null, null, null, null));

        QuoteBuiltin builtin = new(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, "99"));

        // A missing number is NOT silence — it's a success with a clear message so the bot replies (the handler
        // only sends a successful, non-empty value).
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("I couldn't find quote #99.");
    }

    [Fact]
    public async Task Quote_NoQuotesYet_RepliesWithAFriendlyMessage()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);

        QuoteBuiltin builtin = new(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, string.Empty));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("There are no quotes yet.");
    }
}
