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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The ad schedule of ONE render: Twitch is asked at most once, however many <c>ad.*</c> tokens use it.</summary>
internal sealed class AdData
{
    private const string ItemKey = "ad.data";

    private readonly BroadcastMemo<TwitchAdSchedule?> _schedule;

    private AdData(IServiceScopeFactory scopes, Guid broadcasterId) =>
        _schedule = new(ct =>
            BroadcastFetch.RunAsync<ITwitchAdsApi, TwitchAdSchedule>(
                scopes,
                (api, token) => api.GetAdScheduleAsync(broadcasterId, token),
                ct
            )
        );

    /// <summary>The render's ad data; null when the render has no channel.</summary>
    public static AdData? For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is AdData data)
            return data;

        AdData created = new(scopes, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The channel's ad schedule; null when Twitch does not answer.</summary>
    public Task<TwitchAdSchedule?> ScheduleAsync(CancellationToken ct) => _schedule.GetAsync(ct);
}
