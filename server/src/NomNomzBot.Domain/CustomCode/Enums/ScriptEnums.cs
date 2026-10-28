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

namespace NomNomzBot.Domain.CustomCode.Enums;

/// <summary>The terminal outcome of one sandboxed script execution (custom-code.md §4).</summary>
public enum ScriptExecutionOutcome
{
    Success,
    Faulted,
    Timeout,
    HostBudgetExceeded,
    Denied,
}

/// <summary>Why a script run was denied (custom-code.md §4).</summary>
public enum ScriptDenialReason
{
    CapabilityDenied,
    QuotaExceeded,
    EgressBlocked,
    ScriptDisabled,
    VersionInvalid,
}
