// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// A hand-written token resolver fake that counts refreshes and hands back a controllable access token,
/// so a transport test can prove the 401→single-refresh-and-retry path uses the <em>refreshed</em> token.
/// </summary>
public sealed class FakeTwitchTokenResolver : ITwitchTokenResolver
{
    public string CurrentToken { get; set; } = "initial-token";
    public string RefreshedToken { get; set; } = "refreshed-token";
    public bool RefreshSucceeds { get; set; } = true;
    public int RefreshCallCount { get; private set; }
    public Guid? BroadcasterId { get; set; } = Guid.Parse("0195e0d2-1111-7111-8111-000000000001");

    public Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default) =>
        Task.FromResult(
            Result.Success(new TwitchAccessContext(CurrentToken, null, "twitch_bot", "helix:bot"))
        );

    public Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Result.Success(
                new TwitchAccessContext(CurrentToken, broadcasterId, "twitch", "helix:user")
            )
        );

    public Task<Result<TwitchAccessContext>> GetUserTokenAsync(
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Result.Success(
                new TwitchAccessContext(CurrentToken, BroadcasterId, "twitch", "helix:user")
            )
        );

    public Task<Result<TwitchAccessContext>> RefreshAsync(
        TwitchAccessContext context,
        CancellationToken ct = default
    )
    {
        RefreshCallCount++;
        if (!RefreshSucceeds)
            return Task.FromResult(
                Result.Failure<TwitchAccessContext>("refresh failed", TwitchErrorCodes.Unauthorized)
            );

        return Task.FromResult(Result.Success(context with { AccessToken = RefreshedToken }));
    }

    public Task<bool> HasScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct = default
    ) => Task.FromResult(true);
}
