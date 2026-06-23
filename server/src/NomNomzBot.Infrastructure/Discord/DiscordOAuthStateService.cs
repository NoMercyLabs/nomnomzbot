// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Cache-backed implementation of <see cref="IDiscordOAuthStateService"/> (the Discord-flow analogue of
/// <c>TwitchOAuthStateService</c>). A 256-bit random nonce keys the flow payload in the shared cache (Redis in
/// deployment) with a 10-minute TTL, and consumption removes it so a nonce works exactly once.
/// </summary>
public sealed class DiscordOAuthStateService : IDiscordOAuthStateService
{
    private const string CachePrefix = "discord:oauth:state:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ICacheService _cache;

    public DiscordOAuthStateService(ICacheService cache) => _cache = cache;

    public async Task<string> IssueAsync(
        DiscordOAuthFlowState state,
        CancellationToken ct = default
    )
    {
        string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await _cache.SetAsync(CachePrefix + nonce, state, Ttl, ct);
        return nonce;
    }

    public async Task<DiscordOAuthFlowState?> ConsumeAsync(
        string? stateNonce,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(stateNonce))
            return null;

        string key = CachePrefix + stateNonce;
        DiscordOAuthFlowState? state = await _cache.GetAsync<DiscordOAuthFlowState>(key, ct);
        if (state is not null)
            await _cache.RemoveAsync(key, ct); // single-use
        return state;
    }
}
