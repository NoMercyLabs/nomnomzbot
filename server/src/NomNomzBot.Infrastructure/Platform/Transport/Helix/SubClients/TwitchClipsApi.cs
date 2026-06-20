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
/// The Helix "Clips" sub-client (twitch-helix.md §3). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// Both clip-create endpoints carry their inputs as query parameters (not a JSON body), matching the Twitch
/// wire shape. It deliberately holds no database or event-bus dependency — mirroring Twitch state into local
/// tables and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchClipsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchClipsApi
{
    public async Task<Result<TwitchClipStub>> CreateClipAsync(
        Guid broadcasterId,
        bool? hasDelay,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ClipsEdit, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchClipStub>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchClipStub>(default!);

        List<KeyValuePair<string, string>> query = [new("broadcaster_id", channel.Value)];
        if (hasDelay is not null)
            query.Add(new("has_delay", hasDelay.Value ? "true" : "false"));

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "clips",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchClipStub>(request, ct);
    }

    public async Task<Result<TwitchClipStub>> CreateClipFromVodAsync(
        Guid broadcasterId,
        CreateClipFromVodRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.EditorManageClips, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchClipStub>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchClipStub>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("editor_id", request.EditorId),
            new("vod_id", request.VodId),
            new("vod_offset", request.VodOffset.ToString()),
            new("title", request.Title),
        ];
        if (request.Duration is not null)
            query.Add(new("duration", request.Duration.Value.ToString()));

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "videos/clips",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchClipStub>(helixRequest, ct);
    }

    public async Task<Result<TwitchPage<TwitchClip>>> GetClipsByBroadcasterAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchPage<TwitchClip>>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "clips",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: query
        );

        return await transport.GetPageAsync<TwitchClip>(request, ct);
    }

    public async Task<Result<IReadOnlyList<TwitchClip>>> GetClipsByIdsAsync(
        IReadOnlyList<string> clipIds,
        CancellationToken ct = default
    )
    {
        List<KeyValuePair<string, string>> query = [];
        foreach (string clipId in clipIds)
            query.Add(new("id", clipId));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "clips",
            TwitchHelixAuth.App,
            Query: query
        );

        return await transport.GetListAsync<TwitchClip>(request, ct);
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
