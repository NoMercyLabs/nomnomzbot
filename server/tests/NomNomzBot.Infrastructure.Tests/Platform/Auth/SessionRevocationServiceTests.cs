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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Infrastructure.Platform.Auth;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Auth;

/// <summary>
/// Proves the S098b revocation contract: revoking a session makes <c>IsRevokedAsync</c> true for that
/// session (and only that session), and repeat lookups within the local cache window hit the durable
/// <see cref="ICacheService"/> store at most once.
/// </summary>
public class SessionRevocationServiceTests
{
    private static SessionRevocationService Create(ICacheService store, out IMemoryCache localCache)
    {
        localCache = new MemoryCache(new MemoryCacheOptions());
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { { "Jwt:ExpiryMinutes", "60" } }
            )
            .Build();
        return new(store, localCache, config);
    }

    [Fact]
    public async Task IsRevokedAsync_UnknownSession_IsNotRevoked()
    {
        ICacheService store = Substitute.For<ICacheService>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        SessionRevocationService svc = Create(store, out _);

        bool revoked = await svc.IsRevokedAsync(Guid.NewGuid());

        revoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_ThenIsRevokedAsync_ReturnsTrue_ForThatSessionOnly()
    {
        ICacheService store = Substitute.For<ICacheService>();
        HashSet<string> revokedKeys = [];
        store
            .ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => revokedKeys.Contains(callInfo.ArgAt<string>(0)));
        store
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => revokedKeys.Add(callInfo.ArgAt<string>(0)));
        SessionRevocationService svc = Create(store, out _);

        Guid revokedSession = Guid.NewGuid();
        Guid otherSession = Guid.NewGuid();

        await svc.RevokeAsync(revokedSession);

        (await svc.IsRevokedAsync(revokedSession)).Should().BeTrue();
        (await svc.IsRevokedAsync(otherSession)).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_RepeatedCallsWithinCacheWindow_ConsultTheStoreOnlyOnce()
    {
        ICacheService store = Substitute.For<ICacheService>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        SessionRevocationService svc = Create(store, out _);

        Guid sessionId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
            await svc.IsRevokedAsync(sessionId);

        await store.Received(1).ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_TakesEffectImmediately_WithoutWaitingOnTheStore()
    {
        // A revocation must be visible on THIS instance right away — it populates the local cache itself
        // rather than relying on the next IsRevokedAsync call to round-trip the (possibly eventually-
        // consistent) durable store.
        ICacheService store = Substitute.For<ICacheService>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        store
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        SessionRevocationService svc = Create(store, out _);

        Guid sessionId = Guid.NewGuid();
        await svc.RevokeAsync(sessionId);

        (await svc.IsRevokedAsync(sessionId)).Should().BeTrue();
        // The store itself still reports false (e.g. Redis replication lag) — the local cache is what
        // makes revocation immediate on this instance.
        await store.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
