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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The segments still to come and the vacation that is not over yet, as of one moment.</summary>
internal sealed record ScheduleView(
    IReadOnlyList<TwitchScheduleSegment> Upcoming,
    TwitchScheduleVacation? Vacation
);

/// <summary>The stream schedule of ONE render: Twitch is asked at most once, however many <c>schedule.*</c> tokens use it.</summary>
internal sealed class ScheduleData
{
    private const string ItemKey = "schedule.data";
    private const int PageSize = 25;

    private readonly BroadcastMemo<TwitchSchedule?> _schedule;

    private ScheduleData(IServiceScopeFactory scopes, Guid broadcasterId) =>
        _schedule = new(ct =>
            BroadcastFetch.RunAsync<ITwitchScheduleApi, TwitchSchedule>(
                scopes,
                (api, token) =>
                    api.GetScheduleAsync(
                        broadcasterId,
                        new TwitchPageRequest(PageSize: PageSize),
                        token
                    ),
                ct
            )
        );

    /// <summary>The render's schedule data; null when the render has no channel.</summary>
    public static ScheduleData? For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is ScheduleData data)
            return data;

        ScheduleData created = new(scopes, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>
    /// The schedule as of <paramref name="now"/>: canceled segments and segments that ended are dropped, the rest
    /// is ordered by start. Null when Twitch does not answer.
    /// </summary>
    public async Task<ScheduleView?> ViewAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await _schedule.GetAsync(ct) is not { } schedule)
            return null;

        List<TwitchScheduleSegment> upcoming =
        [
            .. schedule
                .Segments.Where(s => s.CanceledUntil is null && s.EndTime > now)
                .OrderBy(s => s.StartTime),
        ];
        TwitchScheduleVacation? vacation = schedule.Vacation is { } v && v.EndTime > now ? v : null;
        return new ScheduleView(upcoming, vacation);
    }
}
