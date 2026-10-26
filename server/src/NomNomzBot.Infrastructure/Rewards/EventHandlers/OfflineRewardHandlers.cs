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
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>Stream ended: turns the channel's rewards off at Twitch unless it keeps them active while offline.</summary>
public sealed class OfflineRewardsOfflineHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<ChannelOfflineEvent>
{
    public async Task HandleAsync(
        ChannelOfflineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IOfflineRewardService>()
            .DisableForOfflineAsync(@event.BroadcasterId, cancellationToken);
    }
}

/// <summary>Stream started: turns back on only the rewards the offline switch turned off.</summary>
public sealed class OfflineRewardsOnlineHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<ChannelOnlineEvent>
{
    public async Task HandleAsync(
        ChannelOnlineEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IOfflineRewardService>()
            .EnableMarkedAsync(@event.BroadcasterId, cancellationToken);
    }
}

/// <summary>The channel settings changed: a channel that now keeps its rewards active gets them back at once.</summary>
public sealed class OfflineRewardsSettingHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<ChannelConfigChangedEvent>
{
    private const string SettingsDomain = "channel-settings";

    public async Task HandleAsync(
        ChannelConfigChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.Equals(@event.Domain, SettingsDomain, StringComparison.OrdinalIgnoreCase))
            return;

        using IServiceScope scope = scopeFactory.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IOfflineRewardService>()
            .ApplyKeepActiveSettingAsync(@event.BroadcasterId, cancellationToken);
    }
}
