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
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// One render's reads of the channel's stream data. Each source is fetched at most once per render and kept
/// (a second ask gets the same task), so a template with ten stream tokens costs one call per source. The
/// providers are singletons and the services are scoped, so every fetch opens its own scope. A failed or empty
/// fetch is null, which prints the token as written.
/// </summary>
internal sealed class StreamHelperData(
    IServiceScopeFactory scopes,
    Guid? broadcasterId,
    string? twitchChannelId
)
{
    private const string ItemsKey = "stream.helper.data";
    private const int RecentPageSize = 5;
    private const int HistoryPageSize = 100;
    private const int StaffPageSize = 100;
    private const int StaffMaxPages = 20;

    private readonly object _gate = new();
    private readonly Dictionary<string, object> _kept = [];
    private HashSet<string> _statsMetrics = [];
    private Task<StreamStatsDto?>? _stats;

    /// <summary>The render's data: made on the first ask and kept in the scope's per-render cache.</summary>
    public static StreamHelperData For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.Items.TryGetValue(ItemsKey, out object? kept))
            return (StreamHelperData)kept;

        StreamHelperData data = new(scopes, scope.BroadcasterId, scope.Channel?.TwitchChannelId);
        scope.Items[ItemsKey] = data;
        return data;
    }

    /// <summary>
    /// The stats of the channel's latest stream for the named metrics. One call serves the union of every metric
    /// asked for in the render: a later ask for a metric not yet fetched fetches the union again.
    /// </summary>
    public Task<StreamStatsDto?> StatsAsync(
        IReadOnlyCollection<string> metrics,
        CancellationToken ct
    )
    {
        lock (_gate)
        {
            if (_stats is not null && _statsMetrics.IsSupersetOf(metrics))
                return _stats;

            _statsMetrics = [.. _statsMetrics, .. metrics];
            string[] wanted = [.. _statsMetrics];
            return _stats = Fetch<IStreamStatsService, StreamStatsDto>(
                (service, token) => service.GetAsync(broadcasterId!.Value, null, wanted, token),
                ct
            );
        }
    }

    /// <summary>The newest few streams, newest first; the first ended one is the last stream.</summary>
    public Task<IReadOnlyList<StreamListItemDto>?> RecentAsync(CancellationToken ct) =>
        Keep("recent", () => StreamsAsync(RecentPageSize, ct));

    /// <summary>The newest 100 streams and the channel's total stream count.</summary>
    public Task<PagedList<StreamListItemDto>?> HistoryAsync(CancellationToken ct) =>
        Keep("history", () => PageOfStreamsAsync(HistoryPageSize, ct));

    /// <summary>The per-stream detail (subscribers) of the last ended stream.</summary>
    public Task<StreamAnalyticsDto?> LastStreamDetailAsync(CancellationToken ct) =>
        Keep(
            "lastDetail",
            async () =>
                await RecentAsync(ct) is { } recent
                && recent.FirstOrDefault(s => s.EndedAt is not null) is { } last
                    ? await Fetch<IChannelAnalyticsService, StreamAnalyticsDto>(
                        (service, token) =>
                            service.GetStreamAsync(broadcasterId!.Value, last.StreamId, token),
                        ct
                    )
                    : null
        );

    /// <summary>The Twitch category named <paramref name="name"/>, for its box art.</summary>
    public Task<TwitchGame?> GameAsync(string name, CancellationToken ct) =>
        Keep(
            "game:" + name,
            async () =>
                (
                    await Fetch<ITwitchGamesApi, IReadOnlyList<TwitchGame>>(
                        (service, token) => service.GetGamesAsync(null, [name], null, token),
                        ct
                    )
                )?.FirstOrDefault()
        );

    public Task<TwitchChannelInformation?> ChannelInfoAsync(CancellationToken ct) =>
        Keep(
            "info",
            () =>
                Fetch<ITwitchChannelsApi, TwitchChannelInformation>(
                    (service, token) =>
                        service.GetChannelInformationAsync(broadcasterId!.Value, token),
                    ct
                )
        );

    public Task<int?> FollowerCountAsync(CancellationToken ct) =>
        Keep(
            "followers",
            () =>
                FetchNumber<ITwitchChannelsApi>(
                    (service, token) =>
                        service.GetChannelFollowerCountAsync(broadcasterId!.Value, token),
                    ct
                )
        );

    public Task<int?> SubscriberCountAsync(CancellationToken ct) =>
        Keep(
            "subscribers",
            () =>
                FetchNumber<ITwitchSubscriptionsApi>(
                    (service, token) =>
                        service.GetSubscriberCountAsync(broadcasterId!.Value, token),
                    ct
                )
        );

    public Task<IReadOnlyList<TwitchChannelTeam>?> TeamsAsync(CancellationToken ct) =>
        Keep(
            "teams",
            () =>
                Fetch<ITwitchTeamsApi, IReadOnlyList<TwitchChannelTeam>>(
                    (service, token) => service.GetChannelTeamsAsync(broadcasterId!.Value, token),
                    ct
                )
        );

    public Task<int?> EditorCountAsync(CancellationToken ct) =>
        Keep(
            "editors",
            async () =>
                (
                    await Fetch<ITwitchChannelsApi, IReadOnlyList<TwitchChannelEditor>>(
                        (service, token) =>
                            service.GetChannelEditorsAsync(broadcasterId!.Value, token),
                        ct
                    )
                )?.Count
        );

    public Task<int?> ModeratorCountAsync(CancellationToken ct) =>
        Keep(
            "mods",
            () =>
                CountPagesAsync<ITwitchModeratorsApi, TwitchModerator>(
                    (service, page, token) =>
                        service.GetModeratorsAsync(broadcasterId!.Value, page, token),
                    ct
                )
        );

    public Task<int?> VipCountAsync(CancellationToken ct) =>
        Keep(
            "vips",
            () =>
                CountPagesAsync<ITwitchModeratorsApi, TwitchVip>(
                    (service, page, token) =>
                        service.GetVipsAsync(broadcasterId!.Value, page, token),
                    ct
                )
        );

    /// <summary>The channel's latest past broadcast (a VOD), or null when it has none.</summary>
    public Task<TwitchVideo?> LatestVideoAsync(CancellationToken ct) =>
        Keep(
            "video",
            async () =>
                (
                    await Fetch<ITwitchVideosApi, TwitchPage<TwitchVideo>>(
                        (service, token) =>
                            service.GetVideosByBroadcasterAsync(
                                broadcasterId!.Value,
                                "archive",
                                null,
                                null,
                                new TwitchPageRequest(PageSize: 1),
                                token
                            ),
                        ct
                    )
                )?.Items.FirstOrDefault()
        );

    public Task<RaidHistory?> RaidHistoryAsync(CancellationToken ct) =>
        twitchChannelId is not { } ownId
            ? Task.FromResult<RaidHistory?>(null)
            : Keep(
                "raids",
                () =>
                    Fetch<IRaidHistoryReader, RaidHistory>(
                        (service, token) =>
                            service.GetHistoryAsync(broadcasterId!.Value, ownId, token),
                        ct
                    )
            );

    /// <summary>The Twitch account of the channel itself (its creation date).</summary>
    public Task<TwitchUser?> OwnerAsync(CancellationToken ct) =>
        twitchChannelId is not { } ownId
            ? Task.FromResult<TwitchUser?>(null)
            : UserByIdAsync(ownId, ct);

    public Task<TwitchUser?> UserByIdAsync(string twitchUserId, CancellationToken ct) =>
        Keep(
            "user-id:" + twitchUserId,
            async () =>
                (
                    await Fetch<ITwitchUsersApi, IReadOnlyList<TwitchUser>>(
                        (service, token) => service.GetUsersByIdsAsync([twitchUserId], token),
                        ct
                    )
                )?.FirstOrDefault()
        );

    public Task<TwitchUser?> UserByLoginAsync(string login, CancellationToken ct) =>
        Keep(
            "user-login:" + login.ToLowerInvariant(),
            async () =>
                (
                    await Fetch<ITwitchUsersApi, IReadOnlyList<TwitchUser>>(
                        (service, token) => service.GetUsersByLoginsAsync([login], token),
                        ct
                    )
                )?.FirstOrDefault()
        );

    private async Task<IReadOnlyList<StreamListItemDto>?> StreamsAsync(
        int pageSize,
        CancellationToken ct
    ) => (await PageOfStreamsAsync(pageSize, ct))?.Items;

    private Task<PagedList<StreamListItemDto>?> PageOfStreamsAsync(
        int pageSize,
        CancellationToken ct
    ) =>
        Fetch<IChannelAnalyticsService, PagedList<StreamListItemDto>>(
            (service, token) =>
                service.ListStreamsAsync(
                    broadcasterId!.Value,
                    new PaginationParams(PageSize: pageSize),
                    token
                ),
            ct
        );

    private async Task<int?> CountPagesAsync<TService, TItem>(
        Func<TService, TwitchPageRequest, CancellationToken, Task<Result<TwitchPage<TItem>>>> read,
        CancellationToken ct
    )
        where TService : notnull
    {
        int count = 0;
        string? cursor = null;
        for (int page = 0; page < StaffMaxPages; page++)
        {
            TwitchPage<TItem>? items = await Fetch<TService, TwitchPage<TItem>>(
                (service, token) =>
                    read(service, new TwitchPageRequest(cursor, StaffPageSize), token),
                ct
            );
            if (items is null)
                return null;

            count += items.Items.Count;
            if (items.NextCursor is null)
                return count;

            cursor = items.NextCursor;
        }

        return count;
    }

    /// <summary>The kept task of a fetch; the first ask starts it. Locked: warm-up fetches run side by side.</summary>
    private Task<T> Keep<T>(string key, Func<Task<T>> fetch)
    {
        lock (_gate)
        {
            if (_kept.TryGetValue(key, out object? kept))
                return (Task<T>)kept;

            Task<T> task = fetch();
            _kept[key] = task;
            return task;
        }
    }

    private async Task<TValue?> Fetch<TService, TValue>(
        Func<TService, CancellationToken, Task<Result<TValue>>> read,
        CancellationToken ct
    )
        where TService : notnull
        where TValue : class
    {
        Result<TValue>? result = await Run(read, ct);
        return result is { IsSuccess: true } ? result.Value : null;
    }

    private async Task<int?> FetchNumber<TService>(
        Func<TService, CancellationToken, Task<Result<int>>> read,
        CancellationToken ct
    )
        where TService : notnull
    {
        Result<int>? result = await Run(read, ct);
        return result is { IsSuccess: true } ? result.Value : null;
    }

    /// <summary>
    /// One service call in its own scope. A missing channel or a service that throws reads as no data: a template
    /// render must never fail because one source is down.
    /// </summary>
    private async Task<Result<TValue>?> Run<TService, TValue>(
        Func<TService, CancellationToken, Task<Result<TValue>>> read,
        CancellationToken ct
    )
        where TService : notnull
    {
        if (broadcasterId is null)
            return null;

        try
        {
            using IServiceScope scope = scopes.CreateScope();
            return await read(scope.ServiceProvider.GetRequiredService<TService>(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
