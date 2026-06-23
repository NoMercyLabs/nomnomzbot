// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Quotes.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;

namespace NomNomzBot.Infrastructure.Tests.Quotes;

/// <summary>
/// Behavior tests for the <c>post_quote</c> pipeline action: it must render the canonical line, stow it in the
/// pipeline variables, actually send it to chat, and fail closed (no chat side effect) when the channel has no
/// quotes. These prove the action's consequences, not that it returned non-null.
/// </summary>
public sealed class PostQuoteActionTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    /// <summary>A chat provider that records exactly what was sent so the test can assert the real side effect.</summary>
    private sealed class RecordingChatProvider : IChatProvider
    {
        public List<(Guid Broadcaster, string Message)> Sent { get; } = [];

        public Task SendMessageAsync(
            Guid broadcasterId,
            string message,
            CancellationToken ct = default
        )
        {
            Sent.Add((broadcasterId, message));
            return Task.CompletedTask;
        }

        public Task SendReplyAsync(
            Guid broadcasterId,
            string replyToMessageId,
            string message,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task TimeoutUserAsync(
            Guid broadcasterId,
            string userId,
            int durationSeconds,
            string? reason = null,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task BanUserAsync(
            Guid broadcasterId,
            string userId,
            string? reason = null,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task UnbanUserAsync(
            Guid broadcasterId,
            string userId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task DeleteMessageAsync(
            Guid broadcasterId,
            string messageId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

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

    private static PipelineExecutionContext Context(Guid broadcasterId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            TriggeredByUserId = "999",
            TriggeredByDisplayName = "viewer",
            MessageId = "msg-1",
            RawMessage = "!quote",
        };

    private static ActionDefinition Action(int? number)
    {
        Dictionary<string, JsonElement>? parameters = number is null
            ? null
            : new() { ["number"] = JsonSerializer.SerializeToElement(number.Value) };
        return new ActionDefinition { Type = "post_quote", Parameters = parameters };
    }

    [Fact]
    public async Task PostQuote_RendersCanonicalLine_StowsVariable_AndSendsToChat()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(
            channel,
            new AddQuoteRequest("blame the lag", "Stoney_Eagle", "Just Chatting", null, null)
        );

        RecordingChatProvider chat = new();
        PostQuoteAction action = new(quotes, chat);
        PipelineExecutionContext ctx = Context(channel);

        ActionResult result = await action.ExecuteAsync(ctx, Action(1));

        const string expected = "#1: \"blame the lag\" — Stoney_Eagle (Just Chatting)";
        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        result.Output.Should().Be(expected);

        // The rendered line is stowed for downstream steps.
        ctx.Variables.Should().ContainKey("quote");
        ctx.Variables["quote"].Should().Be(expected);

        // It was actually sent to THIS channel's chat.
        chat.Sent.Should().ContainSingle();
        chat.Sent[0].Broadcaster.Should().Be(channel);
        chat.Sent[0].Message.Should().Be(expected);
    }

    [Fact]
    public async Task PostQuote_WithoutNumber_PostsARandomQuote()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        await quotes.AddAsync(channel, new AddQuoteRequest("only one", null, null, null, null));

        RecordingChatProvider chat = new();
        PostQuoteAction action = new(quotes, chat);

        // No "number" config → random; with a single quote, it must be #1.
        ActionResult result = await action.ExecuteAsync(Context(channel), Action(null));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        result.Output.Should().Be("#1: \"only one\"");
        chat.Sent.Should().ContainSingle().Which.Message.Should().Be("#1: \"only one\"");
    }

    [Fact]
    public async Task PostQuote_FailsClosed_WhenChannelHasNoQuotes()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);

        RecordingChatProvider chat = new();
        PostQuoteAction action = new(quotes, chat);

        ActionResult result = await action.ExecuteAsync(Context(channel), Action(null));

        // Fails closed: the action reports failure AND posts nothing to chat.
        result.Succeeded.Should().BeFalse();
        chat.Sent.Should().BeEmpty();
    }
}
