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
