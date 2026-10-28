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

using System.Collections.Concurrent;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Pushes "the journal grew" to a channel's dashboards as the generic <c>ConfigChanged</c> signal with domain
/// <see cref="Domain"/> — the journal page reads its list again, no polling. Signals coalesce per channel over
/// <see cref="CoalesceWindow"/>, so a busy chat or an import becomes one push per window. Singleton, since the
/// pending set must outlive any one request.
/// </summary>
public sealed class EventJournalChangeNotifier(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<EventJournalChangeNotifier> logger
) : IEventJournalChangeNotifier
{
    /// <summary>The <c>ConfigChanged</c> domain the dashboard maps to its journal list query.</summary>
    public const string Domain = "event-journal";

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
            // Released BEFORE the push, so an append landing while it is in flight schedules a fresh one.
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
                "Could not push the event journal change for {ChannelId}",
                channelId
            );
        }
    }
}
