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

/// <summary>What one render knows about the channel's predictions: fetched on the first ask and kept for the render.</summary>
internal sealed class LivePredictionData(IServiceScopeFactory scopes, Guid? broadcasterId)
{
    private const string ItemsKey = "live.prediction.data";

    private readonly object _gate = new();
    private Task<TwitchPrediction?>? _current;
    private Task<PollPredictionHistoryItemDto?>? _last;

    public static LivePredictionData For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.Items.TryGetValue(ItemsKey, out object? kept))
            return (LivePredictionData)kept;

        LivePredictionData data = new(scopes, scope.BroadcasterId);
        scope.Items[ItemsKey] = data;
        return data;
    }

    /// <summary>
    /// The prediction that is not finished: open for bets or locked and waiting for the result. Twitch lists
    /// newest first, so it is always the first; a resolved or canceled one is not current.
    /// </summary>
    public Task<TwitchPrediction?> CurrentAsync(CancellationToken ct)
    {
        lock (_gate)
            return _current ??= FetchCurrentAsync(ct);
    }

    /// <summary>The newest finished prediction of the channel's history, or null.</summary>
    public Task<PollPredictionHistoryItemDto?> LastAsync(CancellationToken ct)
    {
        lock (_gate)
            return _last ??= PollPredictionFetch.LastEndedAsync(
                scopes,
                broadcasterId,
                PollPredictionFetch.PredictionKind,
                ct
            );
    }

    private async Task<TwitchPrediction?> FetchCurrentAsync(CancellationToken ct) =>
        (
            await PollPredictionFetch.RunAsync<ITwitchPredictionsApi, TwitchPage<TwitchPrediction>>(
                scopes,
                broadcasterId,
                (service, token) =>
                    service.GetPredictionsAsync(
                        broadcasterId!.Value,
                        null,
                        new TwitchPageRequest(PageSize: 1),
                        token
                    ),
                ct
            )
        )?.Items.FirstOrDefault(PredictionText.IsUnfinished);
}
