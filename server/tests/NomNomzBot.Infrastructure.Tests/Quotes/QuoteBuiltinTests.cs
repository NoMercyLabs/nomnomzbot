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
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
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
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static readonly Guid InvokerId = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");

    private static QuoteService NewQuoteService(QuoteTestDbContext db)
    {
        QuoteTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new(db, allocator, uow, new RecordingEventBus(), Clock);
    }

    /// <summary>The built-in over a real quote service, with the caller resolvable and the capabilities toggled.</summary>
    private static QuoteBuiltin NewBuiltin(
        IQuoteService quotes,
        bool mayWrite = true,
        bool mayDelete = true,
        ITtsDispatchService? tts = null
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

        return new(quotes, users, roles, tts ?? NewPassingTtsDispatch());
    }

    /// <summary>A TTS dispatch mock that always reports success, for tests not exercising the TTS path itself.</summary>
    private static ITtsDispatchService NewPassingTtsDispatch()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "voice-1",
                        "test",
                        10,
                        500,
                        null
                    )
                )
            );
        return tts;
    }

    private static async Task<Guid> SeedChannelAsync(QuoteSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using QuoteTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
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
        string? replyUser = null,
        bool speakWithTts = false
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
            SpeakWithTts = speakWithTts,
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
        await quotes.AddAsync(channel, new("first", null, null, null, null));
        await quotes.AddAsync(
            channel,
            new("blame the lag", "Stoney_Eagle", "Just Chatting", null, null)
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
        await quotes.AddAsync(channel, new("only one", null, null, null, null));

        QuoteBuiltin builtin = NewBuiltin(quotes);

        Result<string> result = await builtin.ExecuteAsync(Context(channel, string.Empty));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#1: \"only one\"");
    }

    /// <summary>
    /// Reproduces the owner-reported bug: <c>!quote &lt;n&gt;</c> returning the wrong quote. Twitch clients
    /// (and some bots working around the duplicate-message filter) append an invisible Unicode character after
    /// the typed text, so the raw arg token is <c>"4\u{E0001}"</c> rather than a clean <c>"4"</c>. A strict
    /// <c>int.TryParse</c> on that token fails, silently falls through to "no number", and answers with a
    /// RANDOM quote instead of #4 — which reads in chat as "the bot never gives me the right quote". Seeds five
    /// quotes with #3 soft-deleted (so the deleted number's gap can never mask an off-by-one) and asserts
    /// <c>!quote 4</c> with the trailing noise still resolves to exactly quote #4's text, not #3, not random.
    /// </summary>
    [Fact]
    public async Task Quote_WithNumber_TrailingInvisibleCharacter_StillResolvesTheExactQuote()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("one", null, null, null, null));
        await quotes.AddAsync(channel, new("two", null, null, null, null));
        await quotes.AddAsync(channel, new("three - the deleted one", null, null, null, null));
        await quotes.AddAsync(channel, new("four - the one we want", null, null, null, null));
        await quotes.AddAsync(channel, new("five", null, null, null, null));
        await quotes.DeleteAsync(channel, 3);

        QuoteBuiltin builtin = NewBuiltin(quotes);

        // "4" followed by Twitch's invisible anti-duplicate tag character (U+E0001 LANGUAGE TAG).
        Result<string> result = await builtin.ExecuteAsync(Context(channel, "4󠀁"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#4: \"four - the one we want\"");
    }

    [Fact]
    public async Task Quote_NumberNotFound_StillReplies_WithAFriendlyMessage()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("only one", null, null, null, null));

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

    // ─── Speak with TTS (S-OBS-12) ─────────────────────────────────────────────

    [Fact]
    public async Task Quote_WithSpeakWithTtsOn_QueuesTheQuoteToTts_AndStillPostsToChat()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(
            channel,
            new("blame the lag", "Stoney_Eagle", "Just Chatting", null, null)
        );

        ITtsDispatchService tts = NewPassingTtsDispatch();
        QuoteBuiltin builtin = NewBuiltin(quotes, tts: tts);

        Result<string> result = await builtin.ExecuteAsync(
            Context(channel, "1", speakWithTts: true)
        );

        // Chat still gets the normal formatted reply …
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#1: \"blame the lag\" — Stoney_Eagle (Just Chatting)");
        // … AND the credited, un-numbered text was actually queued to the TTS orchestrator.
        await tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.BroadcasterId == channel && r.Text == "Stoney_Eagle said: blame the lag"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Quote_WithSpeakWithTtsOff_NeverQueuesToTts()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("only one", null, null, null, null));

        ITtsDispatchService tts = NewPassingTtsDispatch();
        QuoteBuiltin builtin = NewBuiltin(quotes, tts: tts);

        // The default: SpeakWithTts = false.
        Result<string> result = await builtin.ExecuteAsync(Context(channel, "1"));

        result.IsSuccess.Should().BeTrue();
        await tts.DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quote_WithSpeakWithTtsOn_ButDispatchFails_StillPostsTheNormalChatReply()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("no overlay attached", null, null, null, null));

        // Simulates the real failure modes (TTS disabled, no overlay/voice attached, over cap) — the
        // dispatch orchestrator fails closed and nothing is claimed as "spoken".
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TtsDispatchOutcome>(
                    "TTS is disabled for this channel.",
                    "TTS_DISABLED"
                )
            );
        QuoteBuiltin builtin = NewBuiltin(quotes, tts: tts);

        Result<string> result = await builtin.ExecuteAsync(
            Context(channel, "1", speakWithTts: true)
        );

        // The chat reply is completely unaffected by the TTS failure.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#1: \"no overlay attached\"");
    }

    /// <summary>
    /// S-OBS-11 regression guard: a bare <c>!quote</c> (no sub-verb, no number) sent as a chat REPLY must not
    /// silently answer with a random existing quote and ignore the reply — it must capture the REPLIED-TO
    /// message's text and credit its REAL author, exactly like <c>!quote add</c> with no text does.
    /// </summary>
    [Fact]
    public async Task Quote_AsReplyWithNoArgs_CreatesACreditedQuoteFromTheRepliedToMessage()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        // The invoker ("viewer", per Context()) replies to "aaoa_"'s message "get rekt" with bare `!quote`.
        Result<string> reply = await builtin.ExecuteAsync(
            Context(channel, string.Empty, replyBody: "get rekt", replyUser: "aaoa_")
        );

        reply.Value.Should().Be("Added #1: \"get rekt\" — aaoa_");
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("get rekt");
        stored.Value.QuotedDisplayName.Should().Be("aaoa_");
    }

    /// <summary>
    /// A NUMBERED read (<c>!quote &lt;n&gt;</c>) sent as a reply is unambiguous read intent — the reply
    /// context must never hijack it into an add. Only a bare, argument-less <c>!quote</c> is reinterpreted.
    /// </summary>
    [Fact]
    public async Task Quote_WithNumber_AsReply_StillReadsThatQuote_IgnoringTheReplyContext()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("only one", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: true);

        Result<string> result = await builtin.ExecuteAsync(
            Context(channel, "1", replyBody: "unrelated reply text", replyUser: "someone")
        );

        result.Value.Should().Be("#1: \"only one\"");
        // Nothing new was written — the library still holds exactly the one seeded quote.
        Result<QuoteDto> second = await quotes.GetAsync(channel, 2);
        second.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Without write permission, a bare reply-`!quote` degrades to the normal Everyone-level read rather than
    /// surfacing a permission error for what looks like a plain read command.
    /// </summary>
    [Fact]
    public async Task Quote_AsReplyWithNoArgs_WithoutWritePermission_FallsBackToANormalRead()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new("only one", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayWrite: false);

        Result<string> result = await builtin.ExecuteAsync(
            Context(channel, string.Empty, replyBody: "get rekt", replyUser: "aaoa_")
        );

        result.Value.Should().Be("#1: \"only one\"");
        Result<QuoteDto> second = await quotes.GetAsync(channel, 2);
        second.IsFailure.Should().BeTrue();
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
            new("typo heer", "Stoney_Eagle", "Just Chatting", null, null)
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
        await quotes.AddAsync(channel, new("before", null, null, null, null));
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
        await quotes.AddAsync(channel, new("delete me", null, null, null, null));
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
        await quotes.AddAsync(channel, new("keep me", null, null, null, null));
        QuoteBuiltin builtin = NewBuiltin(quotes, mayDelete: false);

        Result<string> reply = await builtin.ExecuteAsync(Context(channel, "del 1"));

        reply.Value.Should().Contain("permission");
        // Still there.
        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("keep me");
    }
}
