// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
