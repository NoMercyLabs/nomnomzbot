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

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Goals" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchGoalsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchGoalsApi
{
    public async Task<Result<IReadOnlyList<TwitchCreatorGoal>>> GetCreatorGoalsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelReadGoals, ct);
        if (scope.IsFailure)
            return scope.WithValue<IReadOnlyList<TwitchCreatorGoal>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<IReadOnlyList<TwitchCreatorGoal>>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "goals",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetListAsync<TwitchCreatorGoal>(request, ct);
    }

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }

    /// <summary>Pre-checks a required user-token scope, short-circuiting with <c>missing_scope</c> when absent.</summary>
    private async Task<Result> RequireScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct
    )
    {
        bool granted = await tokens.HasScopeAsync(broadcasterId, scope, ct);
        return granted
            ? Result.Success()
            : Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
    }
}
