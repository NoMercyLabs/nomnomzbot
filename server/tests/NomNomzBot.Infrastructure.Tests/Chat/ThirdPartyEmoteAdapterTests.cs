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
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the third-party emote step (chat-decoration spec §0/§3.1/§9·5-6): it matches whole word fragments against the
/// channel's <b>cached</b> sets only (never a provider call), is gated per provider by the channel's feature flags,
/// honours 7TV→BTTV→FFZ precedence, and degrades a cache miss to plain text rather than emitting a broken emote.
/// </summary>
public sealed class ThirdPartyEmoteAdapterTests
{
    private const string BroadcasterId = "12345";

    private static ChatEmote Emote(EmoteProvider provider, string code, string id) =>
        new(
            provider,
            id,
            code,
            new Dictionary<string, string> { ["1"] = $"https://cdn/{id}/1x" },
            Animated: false,
            ZeroWidth: false
        );

    private static ChatMessageFragment Text(string text) => new() { Type = "text", Text = text };

    private static ChatDecorationContext Context(
        IReadOnlySet<string> enabled,
        params ChatMessageFragment[] fragments
    ) =>
        new()
        {
            TwitchBroadcasterId = BroadcasterId,
            EnabledFeatures = enabled,
            Fragments = [.. fragments],
        };

    [Fact]
    public async Task Matches_a_cached_channel_emote_and_carries_the_resolved_emote()
    {
        FakeCache cache = new();
        ChatEmote pepe = Emote(EmoteProvider.SevenTv, "PepeLaugh", "7tv-1");
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, BroadcasterId),
            [pepe]
        );

        ChatDecorationContext context = Context(
            new HashSet<string> { "use_7tv" },
            Text("PepeLaugh")
        );
        await new ThirdPartyEmoteAdapter(cache).DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("emote");
        fragment.Text.Should().Be("PepeLaugh");
        fragment.Emote.Should().Be(pepe);
        fragment.Emote!.Provider.Should().Be(EmoteProvider.SevenTv);
        fragment.Emote!.Urls["1"].Should().Be("https://cdn/7tv-1/1x");
    }

    [Fact]
    public async Task A_disabled_provider_is_not_matched()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, BroadcasterId),
            [Emote(EmoteProvider.SevenTv, "PepeLaugh", "7tv-1")]
        );

        // 7TV cached + present, but the channel has not enabled use_7tv.
        ChatDecorationContext context = Context(new HashSet<string>(), Text("PepeLaugh"));
        ThirdPartyEmoteAdapter adapter = new(cache);

        adapter.AppliesTo(context).Should().BeFalse();
        await adapter.DecorateAsync(context);

        context.Fragments.Should().ContainSingle().Which.Type.Should().Be("text");
    }

    [Fact]
    public async Task Seven_tv_wins_a_shared_code_over_bttv()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, BroadcasterId),
            [Emote(EmoteProvider.SevenTv, "Pog", "7tv-pog")]
        );
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.Bttv, BroadcasterId),
            [Emote(EmoteProvider.Bttv, "Pog", "bttv-pog")]
        );

        ChatDecorationContext context = Context(
            new HashSet<string> { "use_7tv", "use_bttv" },
            Text("Pog")
        );
        await new ThirdPartyEmoteAdapter(cache).DecorateAsync(context);

        context
            .Fragments.Should()
            .ContainSingle()
            .Which.Emote!.Provider.Should()
            .Be(EmoteProvider.SevenTv);
    }

    [Fact]
    public async Task A_cache_miss_leaves_the_word_as_plain_text()
    {
        // Provider enabled, but nothing cached — must degrade to text, never an emote with no url (spec §9·13).
        ChatDecorationContext context = Context(
            new HashSet<string> { "use_7tv" },
            Text("PepeLaugh")
        );
        await new ThirdPartyEmoteAdapter(new FakeCache()).DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("text");
        fragment.Emote.Should().BeNull();
    }

    [Fact]
    public async Task Only_the_matching_word_is_converted_surrounding_fragments_untouched()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [Emote(EmoteProvider.SevenTv, "PepeLaugh", "7tv-1")]
        );

        ChatDecorationContext context = Context(
            new HashSet<string> { "use_7tv" },
            Text("hello"),
            Text(" "),
            Text("PepeLaugh")
        );
        await new ThirdPartyEmoteAdapter(cache).DecorateAsync(context);

        context
            .Fragments.Select(fragment => (fragment.Type, fragment.Text))
            .Should()
            .Equal(("text", "hello"), ("text", " "), ("emote", "PepeLaugh"));
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
