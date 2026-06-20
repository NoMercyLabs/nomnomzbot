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
/// The Helix "Polls" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchPollsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchPollsApi
{
    public async Task<Result<TwitchPage<TwitchPoll>>> GetPollsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? pollIds,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelReadPolls, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchPoll>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchPoll>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (pollIds is not null)
            foreach (string pollId in pollIds)
                query.Add(new("id", pollId));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "polls",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchPoll>(request, ct);
    }

    public async Task<Result<TwitchPoll>> CreatePollAsync(
        Guid broadcasterId,
        CreatePollRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManagePolls, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPoll>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPoll>(default!);

        CreatePollRequest body = request with { BroadcasterId = channel.Value };

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "polls",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: body,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPoll>(helixRequest, ct);
    }

    public async Task<Result<TwitchPoll>> EndPollAsync(
        Guid broadcasterId,
        string pollId,
        string status,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManagePolls, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchPoll>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPoll>(default!);

        EndPollRequest body = new(pollId, status, channel.Value);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "polls",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: body,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPoll>(helixRequest, ct);
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
