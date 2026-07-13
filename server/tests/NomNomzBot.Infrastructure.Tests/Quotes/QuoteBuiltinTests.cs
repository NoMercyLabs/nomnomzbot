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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Quotes.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Quotes;

/// <summary>
/// Behavior tests for the full <c>!quote</c> built-in surface (quotes.md §4). Reading ALWAYS replies (the
/// original bug: <c>!quote 1</c> never answered). Mutating sub-commands — <c>add</c> (incl. reply-capture),
/// <c>edit</c>/<c>update</c>, <c>del</c>/<c>remove</c> — actually persist through the real <see cref="QuoteService"/>
/// and gate on the <c>quotes:write</c> / <c>quotes:delete</c> capabilities, so an unpermitted viewer changes
/// nothing.
/// </summary>
public sealed class QuoteBuiltinTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static readonly Guid InvokerId = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");

    private static QuoteService NewQuoteService(QuoteTestDbContext db)
    {
        QuoteTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new QuoteService(db, allocator, uow, new RecordingEventBus(), Clock);
    }

    /// <summary>The built-in over a real quote service, with the caller resolvable and the capabilities toggled.</summary>
    private static QuoteBuiltin NewBuiltin(
        IQuoteService quotes,
        bool mayWrite = true,
        bool mayDelete = true
    )
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(UserDtoFor(InvokerId)));

        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "quotes:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(mayWrite));
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "quotes:delete",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(mayDelete));

        return new QuoteBuiltin(quotes, users, roles);
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

    private static BuiltinCommandContext Context(
        Guid broadcasterId,
        string args,
        string? replyBody = null,
        string? replyUser = null
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            TriggeringUserId = "999",
            TriggeringUserLogin = "viewer",
            TriggeringUserDisplayName = "viewer",
            Args = args,
            ReplyParentMessageBody = replyBody,
            ReplyParentUserName = replyUser,
        };

    private static UserDto UserDtoFor(Guid id) =>
        new(
            Id: id.ToString(),
            Username: "invoker",
            DisplayName: "Invoker",
            ProfileImageUrl: null,
            Email: null,
            CreatedAt: DateTime.UnixEpoch,
            LastLoginAt: DateTime.UnixEpoch
        );

    // ─── Read ────────────────────────────────────────────────────────────────

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

        QuoteBuiltin builtin = NewBuiltin(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, "2"));

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

        QuoteBuiltin builtin = NewBuiltin(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, string.Empty));

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

        QuoteBuiltin builtin = NewBuiltin(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, "99"));

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

        QuoteBuiltin builtin = NewBuiltin(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, string.Empty));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("There are no quotes yet.");
    }

    // ─── Add ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_WithText_ByAPermittedMod_PersistsAndConfirms()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        Result<string> reply = await builtin.ExecuteAsync(
            Context(channel, "add Mista Fillybilly is an airplane")
        );

        // The reply confirms with the new numbered line …
        reply.Value.Should().Be("Added #1: \"Mista Fillybilly is an airplane\"");
        // … and the quote is really in the library (the whole point that was broken).
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("Mista Fillybilly is an airplane");
    }

    [Fact]
    public async Task Add_AsReplyWithNoText_CapturesTheRepliedToMessageAndAttributesItsAuthor()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        // A reply to "aaoa_" whose message was "get rekt", with a bare `!quote add`.
        Result<string> reply = await builtin.ExecuteAsync(
            Context(channel, "add", replyBody: "get rekt", replyUser: "aaoa_")
        );

        reply.Value.Should().Be("Added #1: \"get rekt\" — aaoa_");
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.Value.Text.Should().Be("get rekt");
        stored.Value.QuotedDisplayName.Should().Be("aaoa_");
    }

    [Fact]
    public async Task Add_WithoutWritePermission_IsRefused_AndPersistsNothing()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: false);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "add not allowed"));

        reply.Value.Should().Contain("permission");
        // Nothing was written — the library is still empty.
        Result<QuoteDto> any = await quotes.GetRandomAsync(channel);
        any.IsFailure.Should().BeTrue();
        any.ErrorCode.Should().Be("QUOTES_EMPTY");
    }

    // ─── Edit / Update ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit_RebodiesTheQuote_KeepingItsAttribution()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(
            channel,
            new AddQuoteRequest("typo heer", "Stoney_Eagle", "Just Chatting", null, null)
        );
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "edit 1 typo here"));

        reply.Value.Should().Be("Updated #1: \"typo here\" — Stoney_Eagle (Just Chatting)");
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.Value.Text.Should().Be("typo here");
        stored.Value.QuotedDisplayName.Should().Be("Stoney_Eagle");
        stored.Value.ContextGame.Should().Be("Just Chatting");
    }

    [Fact]
    public async Task Update_IsAnAliasForEdit()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("before", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "update 1 after"));

        reply.Value.Should().Be("Updated #1: \"after\"");
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.Value.Text.Should().Be("after");
    }

    // ─── Delete / Remove ────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_SoftDeletesTheQuote()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("delete me", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayDelete: true);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "remove 1"));

        reply.Value.Should().Be("Deleted quote #1.");
        Result<QuoteDto> gone = await quotes.GetAsync(channel, 1);
        gone.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_WithoutDeletePermission_IsRefused_AndTheQuoteStays()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("keep me", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayDelete: false);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "del 1"));

        reply.Value.Should().Contain("permission");
        // Still there.
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("keep me");
    }
}
