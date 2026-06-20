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
/// The Helix "Ads" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchAdsApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchAdsApi
{
    public async Task<Result<TwitchCommercial>> StartCommercialAsync(
        Guid broadcasterId,
        int lengthSeconds,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelEditCommercial,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchCommercial>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchCommercial>(default!);

        StartCommercialRequest body = new(lengthSeconds, channel.Value);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "channels/commercial",
            TwitchHelixAuth.User,
            broadcasterId,
            Body: body,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchCommercial>(request, ct);
    }

    public async Task<Result<TwitchAdSchedule>> GetAdScheduleAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelReadAds, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchAdSchedule>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchAdSchedule>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "channels/ads",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetSingleAsync<TwitchAdSchedule>(request, ct);
    }

    public async Task<Result<TwitchAdSnooze>> SnoozeNextAdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(broadcasterId, TwitchScopes.ChannelManageAds, ct);
        if (scope.IsFailure)
            return scope.WithValue<TwitchAdSnooze>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchAdSnooze>(default!);

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "channels/ads/schedule/snooze",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchAdSnooze>(request, ct);
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
