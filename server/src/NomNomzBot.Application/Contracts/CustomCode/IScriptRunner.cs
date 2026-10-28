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
