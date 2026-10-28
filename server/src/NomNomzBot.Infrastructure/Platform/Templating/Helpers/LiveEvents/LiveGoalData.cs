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

/// <summary>The render's one creator-goals read; every <c>goal.*</c> token shares it.</summary>
internal sealed class LiveGoalData : ILiveEventData
{
    private const string ItemKey = "goal.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly Guid _broadcasterId;
    private readonly Lazy<Task<IReadOnlyList<TwitchCreatorGoal>>> _goals;

    private LiveGoalData(IServiceScopeFactory scopes, Guid broadcasterId)
    {
        _scopes = scopes;
        _broadcasterId = broadcasterId;
        _goals = new Lazy<Task<IReadOnlyList<TwitchCreatorGoal>>>(LoadAsync);
    }

    /// <summary>The render's one data object; null when the render has no channel.</summary>
    public static LiveGoalData? For(TemplateRenderScope scope, IServiceScopeFactory scopes)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is LiveGoalData data)
            return data;

        LiveGoalData created = new(scopes, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The active goals; empty when the channel has none or Twitch cannot be read.</summary>
    public Task<IReadOnlyList<TwitchCreatorGoal>> GoalsAsync() => _goals.Value;

    /// <summary>The first active goal of the kind; null when the channel has none of that kind.</summary>
    public async Task<TwitchCreatorGoal?> OfKindAsync(GoalKind kind) =>
        (await GoalsAsync()).FirstOrDefault(kind.Matches);

    private async Task<IReadOnlyList<TwitchCreatorGoal>> LoadAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchGoalsApi api = scope.ServiceProvider.GetRequiredService<ITwitchGoalsApi>();
        Result<IReadOnlyList<TwitchCreatorGoal>> goals = await api.GetCreatorGoalsAsync(
            _broadcasterId
        );
        return goals.IsSuccess ? goals.Value : [];
    }
}
