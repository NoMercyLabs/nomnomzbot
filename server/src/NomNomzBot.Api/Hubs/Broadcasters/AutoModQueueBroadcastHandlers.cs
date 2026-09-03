// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Broadcasts a newly AutoMod-held message to dashboard clients as <c>automod_queue_changed</c> (S-OWN22).
/// The Home attention inbox and the Moderation queue panel re-fetch on this signal rather than patching
/// state from the payload. A dashboard-initiated resolve needs no publish of its own — Twitch echoes every
/// resolution as <c>automod.message.update</c>, which reaches
/// <see cref="AutoModMessageUpdatedBroadcastHandler"/> below.
/// </summary>
public sealed class AutoModMessageHeldBroadcastHandler : IEventHandler<AutoModMessageHeldEvent>
{
    private readonly IDashboardNotifier _notifier;

    public AutoModMessageHeldBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(AutoModMessageHeldEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "automod_queue_changed",
            new AutoModQueueChangedAlertDto(@event.MessageId, @event.UserDisplayName, "held"),
            ct
        );
    }
}

/// <summary>
/// Broadcasts a held message's resolution (<c>automod.message.update</c> — a moderator here or elsewhere, or
/// Twitch auto-expiry) as the same <c>automod_queue_changed</c> signal, carrying the raw Twitch verdict.
/// </summary>
public sealed class AutoModMessageUpdatedBroadcastHandler
    : IEventHandler<AutoModMessageUpdatedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public AutoModMessageUpdatedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(AutoModMessageUpdatedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "automod_queue_changed",
            new AutoModQueueChangedAlertDto(
                @event.MessageId,
                @event.UserDisplayName,
                @event.Status
            ),
            ct
        );
    }
}
