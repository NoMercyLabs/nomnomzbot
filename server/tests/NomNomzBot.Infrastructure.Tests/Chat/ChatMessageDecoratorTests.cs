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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the orchestrator (chat-decoration spec §0/§3.1): it runs the real adapter chain end-to-end so a third-party
/// emote in a plain message comes out resolved, it works on COPIES (the event's own fragments are untouched), and it
/// runs adapters in ascending Order, best-effort (a throwing adapter is skipped and the chain continues).
/// </summary>
public sealed class ChatMessageDecoratorTests
{
    [Fact]
    public async Task Runs_the_real_chain_and_resolves_a_third_party_emote_leaving_the_event_untouched()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [
                new ChatEmote(
                    EmoteProvider.SevenTv,
                    "7tv-1",
                    "PepeLaugh",
                    new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
                    Animated: true,
                    ZeroWidth: false
                ),
            ]
        );

        ChatMessageDecorator decorator = Decorator(cache, EmoteChain(cache));

        ChatMessageReceivedEvent evt = Event("PepeLaugh hello");
        DecoratedChatMessage result = await decorator.DecorateAsync(evt);

        result
            .Fragments.Select(fragment => (fragment.Type, fragment.Text))
            .Should()
            .Equal(("emote", "PepeLaugh"), ("text", " hello"));
        ChatEmote emote = result.Fragments[0].Emote!;
        emote.Provider.Should().Be(EmoteProvider.SevenTv);
        emote.Urls["1"].Should().Be("https://cdn.7tv/1x");

        // The event's own fragment list is worked on as copies — still the single un-exploded fragment.
        evt.Fragments.Should().ContainSingle().Which.Text.Should().Be("PepeLaugh hello");
    }

    [Fact]
    public async Task An_explicit_off_toggle_disables_that_provider_so_its_emote_stays_text()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [
                new ChatEmote(
                    EmoteProvider.SevenTv,
                    "7tv-1",
                    "PepeLaugh",
                    new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
                    Animated: true,
                    ZeroWidth: false
                ),
            ]
        );

        // The channel has explicitly turned 7TV off — the cached emote must NOT be matched.
        ChatMessageDecorator decorator = Decorator(cache, EmoteChain(cache), ("use_7tv", false));

        DecoratedChatMessage result = await decorator.DecorateAsync(Event("PepeLaugh hello"));

        result
            .Fragments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<ChatMessageFragment>(fragment =>
                fragment.Type == "text" && fragment.Text == "PepeLaugh hello"
            );
    }

    [Fact]
    public async Task Runs_adapters_in_order_and_skips_a_throwing_one()
    {
        List<int> ran = [];
        IChatDecorationAdapter[] adapters =
        [
            new RecordingAdapter(80, ran),
            new RecordingAdapter(20, ran, throws: true),
            new RecordingAdapter(10, ran),
        ];
        ChatMessageDecorator decorator = Decorator(new FakeCache(), adapters);

        await decorator.DecorateAsync(Event("hello"));

        // Sorted ascending; the throwing step at 20 ran, was caught, and the step at 80 still ran.
        ran.Should().Equal(10, 20, 80);
    }

    // The real emote-resolving chain, sharing the test's cache so seeded sets are visible to the matcher.
    private static IChatDecorationAdapter[] EmoteChain(ICacheService cache) =>
        [
            new ExplodeTextAdapter(),
            new ThirdPartyEmoteAdapter(cache),
            new TwitchEmoteUrlAdapter(),
            new ImplodeTextAdapter(),
        ];

    private static ChatMessageDecorator Decorator(
        ICacheService cache,
        IEnumerable<IChatDecorationAdapter> adapters,
        params (string Key, bool Enabled)[] toggles
    )
    {
        IFeatureService features = Substitute.For<IFeatureService>();
        List<FeatureStatusDto> dtos = toggles
            .Select(toggle => new FeatureStatusDto(toggle.Key, toggle.Enabled, null, []))
            .ToList();
        features
            .GetFeaturesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(dtos)));

        return new ChatMessageDecorator(
            adapters,
            features,
            cache,
            NullLogger<ChatMessageDecorator>.Instance
        );
    }

    private static ChatMessageReceivedEvent Event(string text) =>
        new()
        {
            MessageId = "m1",
            TwitchBroadcasterId = "123",
            UserId = "u1",
            UserDisplayName = "Stoney",
            UserLogin = "stoney_eagle",
            Message = text,
            Fragments = [new ChatMessageFragment { Type = "text", Text = text }],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private sealed class RecordingAdapter(int order, List<int> log, bool throws = false)
        : IChatDecorationAdapter
    {
        public int Order { get; } = order;

        public bool AppliesTo(ChatDecorationContext context) => true;

        public Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
        {
            log.Add(Order);
            if (throws)
                throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }
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
