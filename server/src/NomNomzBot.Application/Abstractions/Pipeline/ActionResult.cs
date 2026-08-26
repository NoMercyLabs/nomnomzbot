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

    /// <summary>Set by a leaf action (e.g. the future <c>wait_for_event</c>) that wants the run
    /// suspended right after it — persisted via <c>PipelineRunState</c> and resumed later rather than
    /// held open in memory (S-PIPE-TREE-d3a). Never set together with a failure.</summary>
    public bool Suspended { get; init; }

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
}
