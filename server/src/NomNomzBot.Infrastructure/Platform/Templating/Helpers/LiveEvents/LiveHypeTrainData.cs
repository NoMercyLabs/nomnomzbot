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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The render's one Hype Train status read; every <c>hypetrain.*</c> token shares it.</summary>
internal sealed class LiveHypeTrainData : ILiveEventData
{
    private const string ItemKey = "hypetrain.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly Guid _broadcasterId;
    private readonly Lazy<Task<TwitchHypeTrainStatus?>> _status;

    private LiveHypeTrainData(IServiceScopeFactory scopes, Guid broadcasterId)
    {
        _scopes = scopes;
        _broadcasterId = broadcasterId;
        _status = new Lazy<Task<TwitchHypeTrainStatus?>>(LoadAsync);
    }

    /// <summary>The render's one data object; null when the render has no channel.</summary>
    public static LiveHypeTrainData? For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is LiveHypeTrainData data)
            return data;

        LiveHypeTrainData created = new(scopes, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The status with its records; null when Twitch has none (a channel with no Hype Train activity).</summary>
    public Task<TwitchHypeTrainStatus?> StatusAsync() => _status.Value;

    /// <summary>The train that runs now; null when none runs.</summary>
    public async Task<TwitchHypeTrain?> CurrentAsync() => (await StatusAsync())?.Current;

    private async Task<TwitchHypeTrainStatus?> LoadAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchHypeTrainApi api = scope.ServiceProvider.GetRequiredService<ITwitchHypeTrainApi>();
        Result<TwitchHypeTrainStatus> status = await api.GetHypeTrainStatusAsync(_broadcasterId);
        return status.IsSuccess ? status.Value : null;
    }
}
