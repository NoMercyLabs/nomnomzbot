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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the mention-colour step (chat-decoration spec §3.1): a mention is coloured with the mentioned user's
/// remembered chat colour, a user the memory has not seen is fetched from Twitch in one batched call and remembered
/// (including "no colour set"), a failed fetch leaves the colour null, and a null colour is never learned from chat.
/// </summary>
public sealed class MentionColorAdapterTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000ee");

    private static ChatDecorationContext Context(params ChatMessageFragment[] fragments) =>
        new() { BroadcasterId = Broadcaster, Fragments = [.. fragments] };

    private static ChatMessageFragment Mention(string userId) =>
        new()
        {
            Type = "mention",
            Text = "@user",
            MentionUserId = userId,
            MentionUserLogin = "user",
            MentionUserName = "User",
        };

    private static ITwitchHelixClient Twitch(
        out ITwitchChatApi chat,
        Result<IReadOnlyList<TwitchUserChatColor>>? answer = null
    )
    {
        chat = Substitute.For<ITwitchChatApi>();
        chat.GetUserChatColorsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(answer ?? Result.Success<IReadOnlyList<TwitchUserChatColor>>([]))
            );
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Chat.Returns(chat);
        return helix;
    }

    private static Result<IReadOnlyList<TwitchUserChatColor>> Colors(
        params TwitchUserChatColor[] colors
    ) => Result.Success<IReadOnlyList<TwitchUserChatColor>>(colors);

    [Fact]
    public async Task Colours_a_mention_with_the_users_remembered_colour()
    {
        ChatColorMemory memory = new(new FakeCache());
        await memory.RememberAsync(Broadcaster, "u1", "#ff8800");
        ITwitchHelixClient helix = Twitch(out ITwitchChatApi chat);

        ChatDecorationContext context = Context(Mention("u1"));
        await new MentionColorAdapter(memory, helix).DecorateAsync(context);

        context.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().Be("#ff8800");
        chat.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Fetches_the_colour_of_a_mentioned_user_the_memory_has_not_seen()
    {
        ChatColorMemory memory = new(new FakeCache());
        ITwitchHelixClient helix = Twitch(
            out _,
            Colors(new TwitchUserChatColor("u9", "thindal", "THINDAL", "#1E90FF"))
        );
        ChatDecorationContext context = Context(Mention("u9"));

        await new MentionColorAdapter(memory, helix).DecorateAsync(context);

        context.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().Be("#1E90FF");
        (await memory.GetAsync(Broadcaster, "u9")).Should().Be("#1E90FF");
    }

    [Fact]
    public async Task Unseen_mentions_in_one_message_make_one_twitch_call()
    {
        ChatColorMemory memory = new(new FakeCache());
        ITwitchHelixClient helix = Twitch(
            out ITwitchChatApi chat,
            Colors(
                new TwitchUserChatColor("u1", "a", "A", "#111111"),
                new TwitchUserChatColor("u2", "b", "B", "#222222")
            )
        );
        ChatDecorationContext context = Context(Mention("u1"), Mention("u2"), Mention("u1"));

        await new MentionColorAdapter(memory, helix).DecorateAsync(context);

        context
            .Fragments.Select(fragment => fragment.MentionColorHex)
            .Should()
            .Equal("#111111", "#222222", "#111111");
        IEnumerable<ICall> calls = chat.ReceivedCalls();
        ICall call = calls.Should().ContainSingle().Subject;
        ((IReadOnlyList<string>)call.GetArguments()[0]!).Should().BeEquivalentTo("u1", "u2");
    }

    [Fact]
    public async Task A_failed_twitch_call_leaves_the_colour_null_and_does_not_throw()
    {
        ChatColorMemory memory = new(new FakeCache());
        ITwitchHelixClient helix = Twitch(
            out _,
            Result.Failure<IReadOnlyList<TwitchUserChatColor>>("boom", "twitch_error")
        );
        ChatDecorationContext context = Context(Mention("u1"));

        Func<Task> act = () => new MentionColorAdapter(memory, helix).DecorateAsync(context);

        await act.Should().NotThrowAsync();
        context.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().BeNull();
        (await memory.RecallAsync(Broadcaster, "u1")).Should().BeNull();
    }

    [Fact]
    public async Task A_user_with_no_colour_is_remembered_so_the_next_message_does_not_refetch()
    {
        ChatColorMemory memory = new(new FakeCache());
        ITwitchHelixClient helix = Twitch(
            out ITwitchChatApi chat,
            Colors(new TwitchUserChatColor("u1", "a", "A", ""))
        );
        MentionColorAdapter adapter = new(memory, helix);

        await adapter.DecorateAsync(Context(Mention("u1")));
        ChatDecorationContext second = Context(Mention("u1"));
        await adapter.DecorateAsync(second);

        second.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().BeNull();
        chat.ReceivedCalls().Should().ContainSingle();
        (await memory.RecallAsync(Broadcaster, "u1"))
            .Should()
            .BeEquivalentTo(new RememberedChatColor(null));
    }

    [Fact]
    public async Task A_null_colour_is_not_remembered_from_chat()
    {
        ChatColorMemory memory = new(new FakeCache());

        await memory.RememberAsync(Broadcaster, "u1", null);

        (await memory.GetAsync(Broadcaster, "u1")).Should().BeNull();
        (await memory.RecallAsync(Broadcaster, "u1")).Should().BeNull();
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }
}
