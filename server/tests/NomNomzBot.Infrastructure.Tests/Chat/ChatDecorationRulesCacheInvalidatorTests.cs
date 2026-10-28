// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Infrastructure.Chat.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Focused unit test of <see cref="ChatDecorationRulesCacheInvalidator"/> in isolation (the broader
/// <c>ChatDecorationFeatureTogglingTests</c> proves it wired into a real <c>EventBus</c>/<c>FeatureService</c>).
/// </summary>
public sealed class ChatDecorationRulesCacheInvalidatorTests
{
    [Fact]
    public async Task A_features_toggle_event_evicts_the_channel_s_cached_decoration_rules()
    {
        Guid channel = Guid.CreateVersion7();
        FakeCache cache = new();
        string key = $"chat:decoration:rules:{channel}";
        await cache.SetAsync(key, new HashSet<string> { "use_7tv" });

        ChatDecorationRulesCacheInvalidator handler = new(cache);
        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Domain = "features",
                EntityId = "use_7tv",
                Action = "toggled",
            }
        );

        (await cache.GetAsync<HashSet<string>>(key)).Should().BeNull();
    }

    [Fact]
    public async Task An_unrelated_domain_s_config_change_never_touches_the_decoration_cache()
    {
        Guid channel = Guid.CreateVersion7();
        FakeCache cache = new();
        string key = $"chat:decoration:rules:{channel}";
        HashSet<string> rules = ["use_7tv", "use_bttv", "use_ffz"];
        await cache.SetAsync(key, rules);

        ChatDecorationRulesCacheInvalidator handler = new(cache);
        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Domain = "timers",
                EntityId = "some-timer-id",
                Action = "updated",
            }
        );

        (await cache.GetAsync<HashSet<string>>(key)).Should().BeSameAs(rules);
    }

    [Fact]
    public async Task A_platform_level_event_with_no_broadcaster_is_a_no_op()
    {
        FakeCache cache = new();
        ChatDecorationRulesCacheInvalidator handler = new(cache);

        // Must not throw building a "chat:decoration:rules:00000000-0000-0000-0000-000000000000" key for a
        // platform-wide (non-tenant) event — it simply has nothing to invalidate.
        Func<Task> act = () =>
            handler.HandleAsync(
                new()
                {
                    BroadcasterId = Guid.Empty,
                    Domain = "features",
                    EntityId = "use_7tv",
                    Action = "toggled",
                }
            );

        await act.Should().NotThrowAsync();
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
