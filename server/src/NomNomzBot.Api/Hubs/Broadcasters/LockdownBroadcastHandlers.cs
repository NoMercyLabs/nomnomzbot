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
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts a lockdown window opening (<c>lockdown_engaged</c>) to dashboard clients.</summary>
public sealed class LockdownEngagedBroadcastHandler : IEventHandler<LockdownEngagedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public LockdownEngagedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(LockdownEngagedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "lockdown_engaged",
            new LockdownEngagedAlertDto(
                @event.WindowId,
                @event.Platform,
                @event.Trigger,
                @event.StartedAt,
                @event.ExpiresAt,
                Names(@event.Engaged),
                Names(@event.Unavailable),
                Names(@event.ApplyFailed)
            ),
            ct
        );
    }

    internal static IReadOnlyList<string> Names(IReadOnlyList<LockdownControl> controls) =>
        controls.Select(c => c.ToString()).ToList();
}

/// <summary>Broadcasts a lockdown window being pushed out (<c>lockdown_extended</c>) to dashboard clients.</summary>
public sealed class LockdownExtendedBroadcastHandler : IEventHandler<LockdownExtendedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public LockdownExtendedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(LockdownExtendedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "lockdown_extended",
            new LockdownExtendedAlertDto(@event.WindowId, @event.Platform, @event.ExpiresAt),
            ct
        );
    }
}

/// <summary>Broadcasts a lockdown window being fully put back (<c>lockdown_restored</c>) to dashboard clients.</summary>
public sealed class LockdownRestoredBroadcastHandler : IEventHandler<LockdownRestoredEvent>
{
    private readonly IDashboardNotifier _notifier;

    public LockdownRestoredBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(LockdownRestoredEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "lockdown_restored",
            new LockdownRestoredAlertDto(
                @event.WindowId,
                @event.Platform,
                @event.RestoredAt,
                LockdownEngagedBroadcastHandler.Names(@event.Restored)
            ),
            ct
        );
    }
}

/// <summary>Broadcasts a restore that left controls tightened (<c>lockdown_restore_failed</c>) to dashboard clients.</summary>
public sealed class LockdownRestoreFailedBroadcastHandler
    : IEventHandler<LockdownRestoreFailedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public LockdownRestoreFailedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(LockdownRestoreFailedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "lockdown_restore_failed",
            new LockdownRestoreFailedAlertDto(
                @event.WindowId,
                @event.Platform,
                LockdownEngagedBroadcastHandler.Names(@event.Failed)
            ),
            ct
        );
    }
}
