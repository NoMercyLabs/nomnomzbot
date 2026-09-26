// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Pushes the action-required invalidation to a channel's dashboards as the generic E5
/// <c>ConfigChanged</c> signal with domain <see cref="Domain"/> — the client refetches its inbox, no polling.
/// <para>
/// Signals coalesce per channel over <see cref="CoalesceWindow"/>: a burst (sixty refused EventSub topics on one
/// reconnect, a webhook retrying) becomes ONE push. The trailing window also covers state that a bus handler
/// writes just after the triggering event was journaled, so the refetch reads the settled state. Singleton,
/// since the pending set must outlive any one request.
/// </para>
/// </summary>
public sealed class ActionRequiredChangeNotifier(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ActionRequiredChangeNotifier> logger
) : IActionRequiredChangeNotifier
{
    /// <summary>The <c>ConfigChanged</c> domain the dashboard maps to its action-required inbox query.</summary>
    public const string Domain = "notifications";

    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public void NotifyChanged(Guid channelId)
    {
        if (channelId == Guid.Empty || !_pending.TryAdd(channelId, 0))
            return;

        _ = PushAfterWindowAsync(channelId);
    }

    private async Task PushAfterWindowAsync(Guid channelId)
    {
        try
        {
            await Task.Delay(CoalesceWindow, clock);
            // Released BEFORE the push, so a change landing while it is in flight schedules a fresh one.
            _pending.TryRemove(channelId, out _);

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IDashboardNotifier notifier =
                scope.ServiceProvider.GetRequiredService<IDashboardNotifier>();
            string channel = channelId.ToString();
            await notifier.SendConfigChangedAsync(
                channel,
                new ConfigChangedDto(channel, Domain, null, "updated")
            );
        }
        catch (Exception ex)
        {
            _pending.TryRemove(channelId, out _);
            logger.LogWarning(
                ex,
                "Could not push the action-required invalidation for {ChannelId}",
                channelId
            );
        }
    }
}
