// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.AutomationApi.Events;

/// <summary>
/// The public <c>obs.streaming.changed</c> automation event (S-STREAMDECK-OBS-REMAINDER) — wraps
/// <see cref="ObsStreamingStateChangedEvent"/>, published by
/// <see cref="Infrastructure.Obs.EventHandlers.ObsAutomationStateForwarder"/> whenever OBS-WS's
/// <c>StreamStateChanged</c> arrives. Mirrors <see cref="SongChangedAutomationEventDescriptor"/>'s
/// field-for-field passthrough shape so the wire payload never drifts from the source event.
/// </summary>
public sealed class ObsStreamingStateAutomationEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "obs.streaming.changed";
    public string Description => "The broadcaster's OBS streaming output started or stopped.";
    public Type DomainEventType => typeof(ObsStreamingStateChangedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ObsStreamingStateChangedEvent e = (ObsStreamingStateChangedEvent)domainEvent;
        return new AutomationObsStreamingStateDto(e.Active);
    }
}

/// <summary>
/// The public <c>obs.recording.changed</c> automation event (S-STREAMDECK-OBS-REMAINDER) — wraps
/// <see cref="ObsRecordingStateChangedEvent"/>, published whenever OBS-WS's <c>RecordStateChanged</c>
/// arrives.
/// </summary>
public sealed class ObsRecordingStateAutomationEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "obs.recording.changed";
    public string Description =>
        "The broadcaster's OBS recording output started, stopped, paused, or resumed.";
    public Type DomainEventType => typeof(ObsRecordingStateChangedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ObsRecordingStateChangedEvent e = (ObsRecordingStateChangedEvent)domainEvent;
        return new AutomationObsRecordingStateDto(e.Active, e.Paused);
    }
}

/// <summary>
/// The public <c>obs.mute.changed</c> automation event (S-STREAMDECK-OBS-REMAINDER) — wraps
/// <see cref="ObsInputMuteStateChangedEvent"/>, published whenever OBS-WS's
/// <c>InputMuteStateChanged</c> arrives.
/// </summary>
public sealed class ObsMuteStateAutomationEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "obs.mute.changed";
    public string Description => "One of the broadcaster's OBS audio inputs was muted or unmuted.";
    public Type DomainEventType => typeof(ObsInputMuteStateChangedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ObsInputMuteStateChangedEvent e = (ObsInputMuteStateChangedEvent)domainEvent;
        return new AutomationObsMuteStateDto(e.InputName, e.Muted);
    }
}
