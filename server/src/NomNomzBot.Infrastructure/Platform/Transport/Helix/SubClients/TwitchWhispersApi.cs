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

using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Whispers" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch user id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchWhispersApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchWhispersApi
{
    [RequiresTwitchScope(TwitchScopes.UserManageWhispers)]
    public async Task<Result> SendWhisperAsync(
        Guid fromUserId,
        string toTwitchUserId,
        string message,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(fromUserId, TwitchScopes.UserManageWhispers, ct);
        if (scope.IsFailure)
            return scope;

        Result<string> user = await ResolveUserAsync(fromUserId, ct);
        if (user.IsFailure)
            return user;

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "whispers",
            TwitchHelixAuth.User,
            fromUserId,
            Query: [new("from_user_id", user.Value), new("to_user_id", toTwitchUserId)],
            Body: new SendWhisperRequest(message),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    [RequiresTwitchScope(TwitchScopes.UserManageWhispers)]
    public async Task<Result> SendAsBotAsync(
        Guid channelId,
        string botTwitchUserId,
        string toTwitchUserId,
        string message,
        CancellationToken ct = default
    )
    {
        bool granted = await tokens.HasBotScopeAsync(
            channelId,
            TwitchScopes.UserManageWhispers,
            ct
        );
        if (!granted)
            return Result.Failure(
                $"Missing required scope '{TwitchScopes.UserManageWhispers}'.",
                TwitchErrorCodes.MissingScope
            );

        // No tenant: the transport's default branch signs with the bot's own user token.
        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "whispers",
            TwitchHelixAuth.App,
            Query: [new("from_user_id", botTwitchUserId), new("to_user_id", toTwitchUserId)],
            Body: new SendWhisperRequest(message),
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch user id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveUserAsync(Guid fromUserId, CancellationToken ct)
    {
        string? userId = await identity.GetTwitchUserIdAsync(fromUserId, ct);
        return userId is null
            ? Result.Failure<string>("User is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(userId);
    }

    /// <summary>Pre-checks a required user-token scope, short-circuiting with <c>missing_scope</c> when absent.</summary>
    private async Task<Result> RequireScopeAsync(
        Guid fromUserId,
        string scope,
        CancellationToken ct
    )
    {
        bool granted = await tokens.HasScopeAsync(fromUserId, scope, ct);
        return granted
            ? Result.Success()
            : Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
    }
}
