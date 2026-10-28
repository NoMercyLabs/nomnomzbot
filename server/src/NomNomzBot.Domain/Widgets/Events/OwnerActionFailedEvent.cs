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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>
/// An action run as the channel owner (from a widget, a script or the dashboard preview) started and then failed:
/// the action reported an error, threw, or ran too long. The streamer's own overlay or script did not do what it
/// was built to do (a muted OBS input that stayed loud, a sound that never played), so the streamer has to know.
/// Published so the action-required inbox can show it (the journal keeps it). A run that never started (unknown
/// action, no permission, rate limited) is not this event, because the caller gets that refusal directly.
/// </summary>
public sealed class OwnerActionFailedEvent : DomainEventBase
{
    /// <summary>The type of the action that failed, for example <c>obs_input_mute</c>.</summary>
    public required string ActionType { get; init; }

    /// <summary>Who ran it: the widget name, or the script name.</summary>
    public required string Caller { get; init; }

    /// <summary>Where the run came from: <c>widget</c> or <c>script</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Why the action failed, as the action reported it.</summary>
    public required string Error { get; init; }

    /// <summary>The machine code of the cause when the action knew it, for example <c>OBS_NOT_CONNECTED</c>.</summary>
    public string? ErrorCode { get; init; }
}
