// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Orchestrates one sandboxed script run for the <c>run_code</c> pipeline action (custom-code.md §3.5):
/// load active version → grant → executor → record. Fail-closed at every gate (disabled / rejected / missing
/// version → Faulted). Keeps the pipeline action thin.
/// </summary>
public interface IScriptRunner
{
    Task<Result<ScriptRunResult>> RunAsync(
        Guid codeScriptId,
        ScriptInvocation invocation,
        CancellationToken cancellationToken = default
    );
}
