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
/// The Helix "Predictions" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
///
/// Unlike most write endpoints, Create / End Prediction carry <c>broadcaster_id</c> in the JSON body rather
/// than the query string. The resolved channel id is therefore folded into a private wire body record so the
/// public request DTOs stay broadcaster-free (the tenant is always the Guid argument, never a caller field).
/// </summary>
public sealed class TwitchPredictionsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchPredictionsApi
{
    public async Task<Result<TwitchPage<TwitchPrediction>>> GetPredictionsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? predictionIds,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelReadPredictions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPage<TwitchPrediction>>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchPrediction>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (predictionIds is not null)
            foreach (string predictionId in predictionIds)
                query.Add(new("id", predictionId));
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "predictions",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchPrediction>(request, ct);
    }

    public async Task<Result<TwitchPrediction>> CreatePredictionAsync(
        Guid broadcasterId,
        CreatePredictionRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManagePredictions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPrediction>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPrediction>(default!);

        CreatePredictionBody body = new(
            channel.Value,
            request.Title,
            request.Outcomes,
            request.PredictionWindow
        );

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "predictions",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: body,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPrediction>(helixRequest, ct);
    }

    public async Task<Result<TwitchPrediction>> EndPredictionAsync(
        Guid broadcasterId,
        string predictionId,
        string status,
        string? winningOutcomeId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManagePredictions,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchPrediction>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPrediction>(default!);

        EndPredictionBody body = new(channel.Value, predictionId, status, winningOutcomeId);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "predictions",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: body,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchPrediction>(helixRequest, ct);
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

    /// <summary>
    /// Create Prediction wire body — the public <see cref="CreatePredictionRequest"/> plus the resolved
    /// broadcaster id, which Twitch requires inside the POST body. Serialized snake_case by the transport.
    /// </summary>
    private sealed record CreatePredictionBody(
        string BroadcasterId,
        string Title,
        IReadOnlyList<CreatePredictionOutcome> Outcomes,
        int PredictionWindow
    );

    /// <summary>
    /// End Prediction wire body — broadcaster id, prediction id, the target status, and the winning outcome
    /// (omitted by the transport when null). Serialized snake_case by the transport.
    /// </summary>
    private sealed record EndPredictionBody(
        string BroadcasterId,
        string Id,
        string Status,
        string? WinningOutcomeId
    );
}
