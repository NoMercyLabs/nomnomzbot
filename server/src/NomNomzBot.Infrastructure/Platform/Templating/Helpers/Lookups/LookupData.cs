// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>A channel's stream right now; <see cref="Stream"/> is null when the channel is offline.</summary>
internal sealed record LiveState(TwitchStream? Stream);

/// <summary>A user's follow record in a channel; <see cref="Follower"/> is null when the user does not follow.</summary>
internal sealed record FollowState(TwitchChannelFollower? Follower);

/// <summary>
/// The Twitch reads of ONE render, through the bot's own Helix client (no outside source). Each read happens at
/// most once per render, however many tokens use it, and is kept for 60 seconds across renders (catalog
/// section 9). A read that fails or passes its 2 second limit is null, which a row prints as its fallback.
/// </summary>
internal sealed class LookupData
{
    private const string ItemKey = "lookup.data";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly LookupCache _cache;
    private readonly Guid? _broadcasterId;
    private readonly Dictionary<string, object> _memo = new(StringComparer.Ordinal);

    private LookupData(IServiceScopeFactory scopes, LookupCache cache, Guid? broadcasterId)
    {
        _scopes = scopes;
        _cache = cache;
        _broadcasterId = broadcasterId;
    }

    public static LookupData For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        LookupCache cache
    )
    {
        if (scope.Items.TryGetValue(ItemKey, out object? existing))
            return (LookupData)existing;

        LookupData data = new(scopes, cache, scope.BroadcasterId);
        scope.Items[ItemKey] = data;
        return data;
    }

    public Task<TwitchUser?> UserAsync(string login, CancellationToken ct) =>
        Once(
            "user:" + login,
            () =>
                _cache.GetOrFetchAsync(
                    "twitch-user",
                    login,
                    Lifetime,
                    () =>
                        Call<ITwitchUsersApi, TwitchUser?>(
                            async (api, c) =>
                                Items(await api.GetUsersByLoginsAsync([login], c))
                                    ?.FirstOrDefault(),
                            ct
                        )
                )
        );

    public async Task<LiveState?> StreamAsync(string login, CancellationToken ct) =>
        await Once(
            "stream:" + login,
            () =>
                _cache.GetOrFetchAsync(
                    "twitch-stream",
                    login,
                    Lifetime,
                    () =>
                        Call<ITwitchStreamsApi, LiveState?>(
                            async (api, c) =>
                                (
                                    await api.GetStreamsAsync(
                                        new TwitchStreamsFilter(UserLogins: [login]),
                                        new TwitchPageRequest(null, 1),
                                        c
                                    )
                                )
                                    is { IsSuccess: true } page
                                    ? new LiveState(page.Value.Items.FirstOrDefault())
                                    : null,
                            ct
                        )
                )
        );

    public async Task<TwitchChannelInformation?> ChannelAsync(string login, CancellationToken ct) =>
        await UserAsync(login, ct) is { } user
            ? await Once(
                "channel:" + user.Id,
                () =>
                    _cache.GetOrFetchAsync(
                        "twitch-channel",
                        user.Id,
                        Lifetime,
                        () =>
                            Call<ITwitchChannelsApi, TwitchChannelInformation?>(
                                async (api, c) =>
                                    Items(
                                        await api.GetChannelInformationByTwitchIdsAsync(
                                            [user.Id],
                                            c
                                        )
                                    )
                                        ?.FirstOrDefault(),
                                ct
                            )
                    )
            )
            : null;

    /// <summary>The follower count of the channel the template runs in; null without a channel.</summary>
    public async Task<int?> FollowerCountAsync(CancellationToken ct) =>
        _broadcasterId is { } id
            ? await Call<ITwitchChannelsApi, int?>(
                async (api, c) =>
                    await api.GetChannelFollowerCountAsync(id, c) is { IsSuccess: true } count
                        ? count.Value
                        : null,
                ct
            )
            : null;

    /// <summary>Whether <paramref name="userTwitchId"/> follows the channel the template runs in, and since when.</summary>
    public async Task<FollowState?> FollowAsync(string userTwitchId, CancellationToken ct) =>
        _broadcasterId is { } id
            ? await Call<ITwitchChannelsApi, FollowState?>(
                async (api, c) =>
                    await api.GetChannelFollowerAsync(id, userTwitchId, c)
                        is { IsSuccess: true } follow
                        ? new FollowState(follow.Value)
                        : null,
                ct
            )
            : null;

    public Task<TwitchGame?> GameAsync(string name, CancellationToken ct) =>
        Once(
            "game:" + name.ToLowerInvariant(),
            () =>
                _cache.GetOrFetchAsync(
                    "twitch-game",
                    name,
                    Lifetime,
                    () =>
                        Call<ITwitchGamesApi, TwitchGame?>(
                            async (api, c) =>
                                Items(await api.GetGamesAsync(null, [name], null, c))
                                    ?.FirstOrDefault(),
                            ct
                        )
                )
        );

    public Task<TwitchGame?> TopGameAsync(CancellationToken ct) =>
        Once(
            "game.top",
            () =>
                _cache.GetOrFetchAsync(
                    "twitch-top-game",
                    "1",
                    Lifetime,
                    () =>
                        Call<ITwitchGamesApi, TwitchGame?>(
                            async (api, c) =>
                                await api.GetTopGamesAsync(new TwitchPageRequest(null, 1), c)
                                    is { IsSuccess: true } page
                                    ? page.Value.Items.FirstOrDefault()
                                    : null,
                            ct
                        )
                )
        );

    private static IReadOnlyList<T>? Items<T>(Result<IReadOnlyList<T>> result) =>
        result.IsSuccess ? result.Value : null;

    private Task<T?> Once<T>(string key, Func<Task<T?>> fetch)
    {
        if (_memo.TryGetValue(key, out object? known))
            return (Task<T?>)known;

        Task<T?> task = fetch();
        _memo[key] = task;
        return task;
    }

    private async Task<TResult?> Call<TApi, TResult>(
        Func<TApi, CancellationToken, Task<TResult?>> read,
        CancellationToken ct
    )
        where TApi : notnull
    {
        using IServiceScope scope = _scopes.CreateScope();
        if (scope.ServiceProvider.GetService<TApi>() is not { } api)
            return default;

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        try
        {
            return await read(api, limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return default;
        }
    }
}
