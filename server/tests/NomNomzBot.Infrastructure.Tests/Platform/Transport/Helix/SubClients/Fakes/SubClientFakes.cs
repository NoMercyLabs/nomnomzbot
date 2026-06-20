// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

/// <summary>
/// A capturing <see cref="ITwitchHelixTransport"/> for sub-client unit tests: it records the last
/// <see cref="TwitchHelixRequest"/> built (so a test asserts verb / path / auth / query / body) and returns
/// pre-seeded canned results — the seam for testing a sub-client's orchestration without any HTTP.
/// </summary>
public sealed class CapturingHelixTransport : ITwitchHelixTransport
{
    public TwitchHelixRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public object? SingleResult { get; set; }
    public object? ListResult { get; set; }
    public object? PageResult { get; set; }
    public int TotalResult { get; set; }
    public Result SendResult { get; set; } = Result.Success();

    public Task<Result<T>> GetSingleAsync<T>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Capture(request);
        return Task.FromResult(Result.Success((T)SingleResult!));
    }

    public Task<Result<IReadOnlyList<T>>> GetListAsync<T>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Capture(request);
        return Task.FromResult(Result.Success((IReadOnlyList<T>)ListResult!));
    }

    public Task<Result<TwitchPage<T>>> GetPageAsync<T>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Capture(request);
        return Task.FromResult(Result.Success((TwitchPage<T>)PageResult!));
    }

    public Task<Result<int>> GetTotalAsync(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Capture(request);
        return Task.FromResult(Result.Success(TotalResult));
    }

    public Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default)
    {
        Capture(request);
        return Task.FromResult(SendResult);
    }

    public Task<Result<T>> SendWithResultAsync<T>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Capture(request);
        return Task.FromResult(Result.Success((T)SingleResult!));
    }

    private void Capture(TwitchHelixRequest request)
    {
        LastRequest = request;
        CallCount++;
    }
}

/// <summary>A fixed <see cref="ITwitchIdentityResolver"/>: one known tenant Guid maps to one Twitch id; all else null.</summary>
public sealed class StubIdentityResolver(Guid knownTenant, string twitchId)
    : ITwitchIdentityResolver
{
    public Task<string?> GetTwitchChannelIdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Task.FromResult(broadcasterId == knownTenant ? twitchId : null);

    public Task<Guid?> GetBroadcasterIdAsync(
        string twitchChannelId,
        CancellationToken ct = default
    ) => Task.FromResult<Guid?>(twitchChannelId == twitchId ? knownTenant : null);

    public Task<Guid?> GetBroadcasterIdByNameAsync(
        string channelName,
        CancellationToken ct = default
    ) => Task.FromResult<Guid?>(null);

    public Task<string?> GetTwitchUserIdAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(userId == knownTenant ? twitchId : null);
}

/// <summary>
/// An <see cref="ITwitchTokenResolver"/> that only answers scope pre-checks (the sole method sub-clients call);
/// token resolution itself is exercised in the transport tests, so those members fail loudly if a sub-client
/// ever reaches for them.
/// </summary>
public sealed class StubScopeTokenResolver(params string[] grantedScopes) : ITwitchTokenResolver
{
    private readonly HashSet<string> _scopes = new(grantedScopes, StringComparer.OrdinalIgnoreCase);

    public Task<bool> HasScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct = default
    ) => Task.FromResult(_scopes.Contains(scope));

    public Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<Result<TwitchAccessContext>> RefreshAsync(
        TwitchAccessContext context,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}
