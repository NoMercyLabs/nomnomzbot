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
