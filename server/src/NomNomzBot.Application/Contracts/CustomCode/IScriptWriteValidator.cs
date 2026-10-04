// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// The argument checks and lookups a write capability runs before it dispatches, without dispatching. A test run
/// calls this instead of the write, so it reports the same <c>nnz.lastError</c> a live run would.
/// </summary>
public interface IScriptWriteValidator
{
    /// <summary>
    /// True when the write may go ahead. On false the last error is set exactly as the live call sets it, and
    /// <paramref name="failureReturn"/> is what the live call returns to the guest. A key that is not a validated
    /// write returns true and leaves the last error empty.
    /// </summary>
    bool ValidateWrite(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct,
        out string? failureReturn
    );
}
