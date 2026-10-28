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

using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Obs.Events;

namespace NomNomzBot.Infrastructure.Obs.EventHandlers;

/// <summary>A test-run sample of one OBS event, built by the same code the live <c>obs_event</c> trigger uses.</summary>
public abstract class ObsEventSampleSource : ITriggerSampleSource
{
    protected abstract string EventType { get; }

    protected abstract string DataJson { get; }

    public TriggerSample Sample(DateTimeOffset now) =>
        new(
            $"obs.{EventType}",
            $"obs.{EventType}",
            null,
            string.Empty,
            ObsEventTriggerSource.BuildVariables(
                new ObsEventReceivedEvent
                {
                    BroadcasterId = Guid.Empty,
                    ObsEventType = EventType,
                    DataJson = DataJson,
                    OccurredAt = now,
                }
            )
        );
}

public sealed class ObsSceneChangedSampleSource : ObsEventSampleSource
{
    protected override string EventType => "CurrentProgramSceneChanged";

    protected override string DataJson =>
        "{\"sceneName\":\"Gameplay\",\"sceneUuid\":\"3f6c1b0e-7d52-4b8a-9a41-2c5e8d7f0a19\"}";
}

public sealed class ObsStreamStateSampleSource : ObsEventSampleSource
{
    protected override string EventType => "StreamStateChanged";

    protected override string DataJson =>
        "{\"outputActive\":true,\"outputState\":\"OBS_WEBSOCKET_OUTPUT_STARTED\"}";
}

public sealed class ObsInputMuteSampleSource : ObsEventSampleSource
{
    protected override string EventType => "InputMuteStateChanged";

    protected override string DataJson =>
        "{\"inputName\":\"Mic/Aux\",\"inputUuid\":\"9b2d4e6f-1a3c-4d5e-8f70-a1b2c3d4e5f6\",\"inputMuted\":true}";
}
