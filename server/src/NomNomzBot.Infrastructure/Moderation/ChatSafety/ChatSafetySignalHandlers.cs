// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Moderation.ChatSafety;

/// <summary>Counts each follow on the channel's safety meter, whether or not spam defense is on.</summary>
public sealed class ChatSafetyFollowHandler : IEventHandler<FollowEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyFollowHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(FollowEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.RecordFollow(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}

/// <summary>Counts a line AutoMod held as a flagged line.</summary>
public sealed class ChatSafetyAutoModHeldHandler : IEventHandler<AutoModMessageHeldEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyAutoModHeldHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(AutoModMessageHeldEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.RecordFlaggedLine(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}

/// <summary>Tells the meter shield mode is on.</summary>
public sealed class ChatSafetyShieldBeganHandler : IEventHandler<ShieldModeBeganEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyShieldBeganHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(ShieldModeBeganEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.SetShieldMode(@event.BroadcasterId, true);

        return Task.CompletedTask;
    }
}

/// <summary>Tells the meter shield mode is off.</summary>
public sealed class ChatSafetyShieldEndedHandler : IEventHandler<ShieldModeEndedEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyShieldEndedHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(ShieldModeEndedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.SetShieldMode(@event.BroadcasterId, false);

        return Task.CompletedTask;
    }
}

/// <summary>Tells the meter a lockdown window is engaged.</summary>
public sealed class ChatSafetyLockdownEngagedHandler : IEventHandler<LockdownEngagedEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyLockdownEngagedHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(LockdownEngagedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.SetLockdown(@event.BroadcasterId, true);

        return Task.CompletedTask;
    }
}

/// <summary>Tells the meter a lockdown window was put back.</summary>
public sealed class ChatSafetyLockdownRestoredHandler : IEventHandler<LockdownRestoredEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyLockdownRestoredHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(LockdownRestoredEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.SetLockdown(@event.BroadcasterId, false);

        return Task.CompletedTask;
    }
}

/// <summary>A new stream starts: every account may count as a new account again.</summary>
public sealed class ChatSafetyStreamStartHandler : IEventHandler<ChannelOnlineEvent>
{
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyStreamStartHandler(ChatSafetyMeter meter) => _meter = meter;

    public Task HandleAsync(ChannelOnlineEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId != Guid.Empty)
            _meter.ResetStream(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}
