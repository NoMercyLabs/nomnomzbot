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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the mention-colour step (chat-decoration spec §3.1): a mention is coloured with the mentioned user's
/// remembered chat colour (learned from that user's own messages), an unseen user is left uncoloured, and a null
/// colour is never remembered.
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

    [Fact]
    public async Task Colours_a_mention_with_the_users_remembered_colour()
    {
        FakeCache cache = new();
        ChatColorMemory memory = new(cache);
        await memory.RememberAsync(Broadcaster, "u1", "#ff8800");

        ChatDecorationContext context = Context(Mention("u1"));
        await new MentionColorAdapter(memory).DecorateAsync(context);

        context.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().Be("#ff8800");
    }

    [Fact]
    public async Task Leaves_an_unseen_user_mention_uncoloured()
    {
        ChatDecorationContext context = Context(Mention("ghost"));

        await new MentionColorAdapter(new ChatColorMemory(new FakeCache())).DecorateAsync(context);

        context.Fragments.Should().ContainSingle().Which.MentionColorHex.Should().BeNull();
    }

    [Fact]
    public async Task A_null_colour_is_not_remembered()
    {
        FakeCache cache = new();
        ChatColorMemory memory = new(cache);

        await memory.RememberAsync(Broadcaster, "u1", null);

        (await memory.GetAsync(Broadcaster, "u1")).Should().BeNull();
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
