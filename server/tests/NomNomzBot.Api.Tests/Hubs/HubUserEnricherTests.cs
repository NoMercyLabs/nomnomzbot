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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Hubs;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the cache-gating <see cref="HubUserEnricher"/> wraps around <see cref="IHubUserEnrichmentStore"/>
/// (GAP E3-2): a burst of hub events for the same viewer collapses to one underlying store call, different
/// viewers each get their own store call, and a store failure degrades to an un-enriched (<c>null</c>) result
/// rather than throwing out of a broadcaster.
/// </summary>
public sealed class HubUserEnricherTests
{
    [Fact]
    public async Task Second_call_for_the_same_viewer_hits_the_cache_not_the_store()
    {
        CountingStore store = new(
            new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "they/them", "Subscriber")
        );
        HubUserEnricher enricher = NewEnricher(store);
        Guid channel = Guid.CreateVersion7();

        HubUserEnrichment? first = await enricher.EnrichAsync(channel, "u1");
        HubUserEnrichment? second = await enricher.EnrichAsync(channel, "u1");

        store.CallCount.Should().Be(1, "the second call within the TTL must be served from cache");
        first.Should().BeSameAs(second);
        first!.AvatarUrl.Should().Be("https://cdn/avatar.png");
        first.Pronouns.Should().Be("they/them");
        first.CommunityStanding.Should().Be("Subscriber");
    }

    [Fact]
    public async Task Different_viewers_each_get_their_own_store_call()
    {
        CountingStore store = new(new HubUserEnrichment("Stoney", null, null, null));
        HubUserEnricher enricher = NewEnricher(store);
        Guid channel = Guid.CreateVersion7();

        await enricher.EnrichAsync(channel, "u1");
        await enricher.EnrichAsync(channel, "u2");

        store.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Different_channels_for_the_same_viewer_each_get_their_own_store_call()
    {
        CountingStore store = new(new HubUserEnrichment("Stoney", null, null, null));
        HubUserEnricher enricher = NewEnricher(store);

        await enricher.EnrichAsync(Guid.CreateVersion7(), "u1");
        await enricher.EnrichAsync(Guid.CreateVersion7(), "u1");

        store
            .CallCount.Should()
            .Be(2, "the cache key must be scoped per broadcaster, not just per viewer");
    }

    [Fact]
    public async Task A_throwing_store_degrades_to_null_and_is_not_cached()
    {
        ThrowingStore store = new();
        HubUserEnricher enricher = NewEnricher(store);
        Guid channel = Guid.CreateVersion7();

        HubUserEnrichment? first = await enricher.EnrichAsync(channel, "u1");
        HubUserEnrichment? second = await enricher.EnrichAsync(channel, "u1");

        first.Should().BeNull();
        second.Should().BeNull();
        store
            .CallCount.Should()
            .Be(2, "a failed lookup must not be cached — the next event gets a fresh attempt");
    }

    [Fact]
    public async Task Empty_twitch_user_id_returns_null_without_calling_the_store()
    {
        CountingStore store = new(new HubUserEnrichment("Stoney", null, null, null));
        HubUserEnricher enricher = NewEnricher(store);

        HubUserEnrichment? result = await enricher.EnrichAsync(Guid.CreateVersion7(), string.Empty);

        result.Should().BeNull();
        store.CallCount.Should().Be(0);
    }

    private static HubUserEnricher NewEnricher(IHubUserEnrichmentStore store) =>
        new(store, new MemoryCache(new MemoryCacheOptions()), NullLogger<HubUserEnricher>.Instance);

    private sealed class CountingStore(HubUserEnrichment result) : IHubUserEnrichmentStore
    {
        public int CallCount { get; private set; }

        public Task<HubUserEnrichment?> LoadAsync(
            Guid broadcasterId,
            string twitchUserId,
            CancellationToken ct = default
        )
        {
            CallCount++;
            return Task.FromResult<HubUserEnrichment?>(result);
        }
    }

    private sealed class ThrowingStore : IHubUserEnrichmentStore
    {
        public int CallCount { get; private set; }

        public Task<HubUserEnrichment?> LoadAsync(
            Guid broadcasterId,
            string twitchUserId,
            CancellationToken ct = default
        )
        {
            CallCount++;
            throw new InvalidOperationException("DB unavailable");
        }
    }
}
