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
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>What one render knows about the channel's polls: fetched on the first ask and kept for the render.</summary>
internal sealed class LivePollData(IServiceScopeFactory scopes, Guid? broadcasterId)
{
    private const string ItemsKey = "live.poll.data";
    private const string ActiveStatus = "ACTIVE";

    private readonly object _gate = new();
    private Task<TwitchPoll?>? _active;
    private Task<PollPredictionHistoryItemDto?>? _last;

    public static LivePollData For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.Items.TryGetValue(ItemsKey, out object? kept))
            return (LivePollData)kept;

        LivePollData data = new(scopes, scope.BroadcasterId);
        scope.Items[ItemsKey] = data;
        return data;
    }

    /// <summary>The poll running now, or null. Twitch lists newest first, so a running poll is always the first.</summary>
    public Task<TwitchPoll?> ActiveAsync(CancellationToken ct)
    {
        lock (_gate)
            return _active ??= FetchActiveAsync(ct);
    }

    /// <summary>The newest finished poll of the channel's history, or null.</summary>
    public Task<PollPredictionHistoryItemDto?> LastAsync(CancellationToken ct)
    {
        lock (_gate)
            return _last ??= PollPredictionFetch.LastEndedAsync(
                scopes,
                broadcasterId,
                PollPredictionFetch.PollKind,
                ct
            );
    }

    private async Task<TwitchPoll?> FetchActiveAsync(CancellationToken ct) =>
        (
            await PollPredictionFetch.RunAsync<ITwitchPollsApi, TwitchPage<TwitchPoll>>(
                scopes,
                broadcasterId,
                (service, token) =>
                    service.GetPollsAsync(
                        broadcasterId!.Value,
                        null,
                        new TwitchPageRequest(PageSize: 1),
                        token
                    ),
                ct
            )
        )?.Items.FirstOrDefault(poll => poll.Status == ActiveStatus);
}
