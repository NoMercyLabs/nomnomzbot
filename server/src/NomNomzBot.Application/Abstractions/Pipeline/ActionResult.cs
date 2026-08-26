// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

// ─── Action result ────────────────────────────────────────────────────────────

public sealed class ActionResult
{
    public bool Succeeded { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Set by a leaf action (e.g. <c>wait_for_event</c>) that wants the run suspended right
    /// after it — persisted via <c>PipelineRunState</c> and resumed later rather than held open in
    /// memory (S-PIPE-TREE-d3a). Never set together with a failure.</summary>
    public bool Suspended { get; init; }

    /// <summary>Set only alongside <see cref="Suspended"/> by <c>wait_for_event</c> (S-PIPE-TREE-d3b) —
    /// the named event this run is now parked waiting for. Persisted onto <c>PipelineRunState.WaitEventName</c>
    /// so a later publish of that event (matched by name, per broadcaster) can find and resume it.</summary>
    public string? WaitEventName { get; init; }

    /// <summary>Set only alongside <see cref="Suspended"/> by <c>wait_for_event</c> — how many seconds
    /// from NOW (the engine's own clock, never the action's) the wait is allowed to stay parked before
    /// it is resumed down the timeout path instead. The action never computes an absolute deadline
    /// itself — only the engine (which owns <c>TimeProvider</c>) does, at persist time.</summary>
    public int? WaitTimeoutSeconds { get; init; }

    public static ActionResult Success(string? output = null) =>
        new() { Succeeded = true, Output = output };

    public static ActionResult Failure(string error) =>
        new() { Succeeded = false, ErrorMessage = error };

    public static ActionResult Suspend(string? output = null) =>
        new()
        {
            Succeeded = true,
            Suspended = true,
            Output = output,
        };

    /// <summary>Suspend variant used by <c>wait_for_event</c> — carries the event name and relative
    /// timeout the engine needs to persist onto <c>PipelineRunState</c> (S-PIPE-TREE-d3b).</summary>
    public static ActionResult SuspendWaitingForEvent(string eventName, int timeoutSeconds) =>
        new()
        {
            Succeeded = true,
            Suspended = true,
            WaitEventName = eventName,
            WaitTimeoutSeconds = timeoutSeconds,
        };
}
