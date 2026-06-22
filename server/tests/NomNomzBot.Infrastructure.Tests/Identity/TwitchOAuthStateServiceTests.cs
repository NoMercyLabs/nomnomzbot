// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the CSRF-safe OAuth state contract: a payload round-trips through an opaque nonce, a nonce is
/// single-use, an unknown/empty nonce is rejected (the callback must fail), and every issue is distinct.
/// </summary>
public sealed class TwitchOAuthStateServiceTests
{
    [Fact]
    public async Task Issue_then_consume_round_trips_the_payload()
    {
        TwitchOAuthStateService svc = new(new FakeCache());

        string nonce = await svc.IssueAsync(
            new TwitchOAuthFlowState("channel_bot", ChannelId: "abc")
        );

        nonce.Should().NotBeNullOrWhiteSpace();
        TwitchOAuthFlowState? consumed = await svc.ConsumeAsync(nonce);
        consumed.Should().NotBeNull();
        consumed!.Flow.Should().Be("channel_bot");
        consumed.ChannelId.Should().Be("abc");
    }

    [Fact]
    public async Task Consume_is_single_use()
    {
        TwitchOAuthStateService svc = new(new FakeCache());
        string nonce = await svc.IssueAsync(new TwitchOAuthFlowState("user"));

        (await svc.ConsumeAsync(nonce)).Should().NotBeNull();
        (await svc.ConsumeAsync(nonce)).Should().BeNull(); // already used — replay rejected
    }

    [Theory]
    [InlineData("never-issued")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Consume_rejects_unknown_or_empty_state(string? nonce)
    {
        TwitchOAuthStateService svc = new(new FakeCache());

        (await svc.ConsumeAsync(nonce)).Should().BeNull();
    }

    [Fact]
    public async Task Each_issue_returns_a_distinct_nonce()
    {
        TwitchOAuthStateService svc = new(new FakeCache());

        string a = await svc.IssueAsync(new TwitchOAuthFlowState("user"));
        string b = await svc.IssueAsync(new TwitchOAuthFlowState("user"));

        a.Should().NotBe(b);
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? v) ? (T?)v : default);

        public Task SetAsync<T>(
            string key,
            T value,
            System.TimeSpan? expiry = null,
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
