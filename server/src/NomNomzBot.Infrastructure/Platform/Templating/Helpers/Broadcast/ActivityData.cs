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
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// The chat activity of ONE render: the in-memory ring for the rolling windows, and each outside source
/// (stream stats, daily analytics, the chatter list, the chat-hours grid) fetched at most once.
/// </summary>
internal sealed class ActivityData
{
    private const string ItemKey = "activity.data";
    private const int BusiestLookbackDays = 90;

    private readonly Guid _channelId;
    private readonly ChatActivityRing _ring;
    private readonly StreamHelperData _stream;
    private readonly BroadcastMemo<long?> _messagesToday;
    private readonly BroadcastMemo<TopViewerDto?> _topToday;
    private readonly BroadcastMemo<long?> _chattersNow;
    private readonly BroadcastMemo<ChannelChatHoursDto?> _chatHours;

    private ActivityData(
        TemplateRenderScope scope,
        Guid channelId,
        IServiceScopeFactory scopes,
        ChatActivityRing ring,
        Random random
    )
    {
        _channelId = channelId;
        _ring = ring;
        Random = random;
        _stream = StreamHelperData.For(scope, scopes);
        DateOnly today = DateOnly.FromDateTime(
            BroadcastText.Local(scope.Now, scope.Timezone).DateTime
        );

        _messagesToday = new(async ct =>
            await BroadcastFetch.RunAsync<
                IChannelAnalyticsService,
                IReadOnlyList<ChannelAnalyticsDailyDto>
            >(scopes, (api, token) => api.GetDailySeriesAsync(channelId, today, today, token), ct)
                is { } days
                ? (long?)days.Sum(d => d.TotalMessages)
                : null
        );
        _topToday = new(async ct =>
            (
                await BroadcastFetch.RunAsync<
                    IChannelAnalyticsService,
                    IReadOnlyList<TopViewerDto>
                >(
                    scopes,
                    (api, token) =>
                        api.GetTopViewersAsync(
                            channelId,
                            TopViewerMetric.Messages,
                            today,
                            today,
                            1,
                            token
                        ),
                    ct
                )
            )?.FirstOrDefault()
        );
        _chattersNow = new(async ct =>
            await BroadcastFetch.RunAsync<ITwitchChatAssetsApi, TwitchPage<TwitchChatter>>(
                scopes,
                (api, token) =>
                    api.GetChattersAsync(channelId, new TwitchPageRequest(PageSize: 1), token),
                ct
            )
                is { } page
                ? (long?)page.Total
                : null
        );
        _chatHours = new(ct =>
            BroadcastFetch.RunAsync<IChannelAnalyticsService, ChannelChatHoursDto>(
                scopes,
                (api, token) =>
                    api.GetChatHoursAsync(
                        channelId,
                        today.AddDays(-BusiestLookbackDays),
                        today,
                        token
                    ),
                ct
            )
        );
    }

    public Random Random { get; }

    /// <summary>The render's activity data; null when the render has no channel.</summary>
    public static ActivityData? For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        ChatActivityRing ring,
        Random random
    )
    {
        if (scope.BroadcasterId is not { } channelId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is ActivityData data)
            return data;

        ActivityData created = new(scope, channelId, scopes, ring, random);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The messages of the last <paramref name="span"/>, oldest first.</summary>
    public IReadOnlyList<ChatActivityEntry> Recent(TimeSpan span) => _ring.Since(_channelId, span);

    /// <summary>The latest stream's stats for one metric; null when there is no stream or the read fails.</summary>
    public Task<StreamStatsDto?> StatsAsync(string metric, CancellationToken ct) =>
        _stream.StatsAsync([metric], ct);

    public Task<long?> MessagesTodayAsync(CancellationToken ct) => _messagesToday.GetAsync(ct);

    public Task<TopViewerDto?> TopTodayAsync(CancellationToken ct) => _topToday.GetAsync(ct);

    public Task<long?> ChattersNowAsync(CancellationToken ct) => _chattersNow.GetAsync(ct);

    public Task<ChannelChatHoursDto?> ChatHoursAsync(CancellationToken ct) =>
        _chatHours.GetAsync(ct);
}
