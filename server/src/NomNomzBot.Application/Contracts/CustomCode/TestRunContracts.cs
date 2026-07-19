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
/// The inputs to a script dry-run (test-run): the seed variables and positional args the author supplies to
/// exercise the script's current version without causing real side effects.
/// </summary>
public sealed record ScriptTestRunRequest(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> Args
);

/// <summary>
/// One outward/mutating effect the real logic tried to perform during a dry-run, RECORDED instead of executed.
/// <c>Name</c> is the capability key (scripts) or the action type (pipelines); <c>ArgsPreview</c> is a short,
/// human-readable rendering of the arguments/config that would have been used.
/// </summary>
public sealed record CapturedEffectDto(string Name, string ArgsPreview);

/// <summary>
/// The uniform result of a script or pipeline test-run (custom-code.md / commands-pipelines.md). The logic ran for
/// real; every outward/mutating effect was CAPTURED (see <see cref="CapturedEffects"/>) rather than committed, while
/// read-only calls (reads, conditions, template resolution, random draws) ran against the live services so the
/// dry-run stays realistic.
/// </summary>
public sealed record TestRunResultDto(
    bool Success,
    string? Error,
    long DurationMs,
    int HostCallCount,
    IReadOnlyList<CapturedEffectDto> CapturedEffects,
    IReadOnlyList<string> ChatOutput,
    IReadOnlyList<string> Log
);

/// <summary>
/// Executes a code-script's current version in CAPTURE mode (custom-code.md §6). The real sandbox runs the compiled
/// JS through the real host bridge, but every side-effecting capability (chat/tts/widget/storage-write/reward/
/// schedule/…) is recorded rather than dispatched; reads run for real. Never mutates the script row, the store, or
/// any external surface.
/// </summary>
public interface IScriptTestRunService
{
    Task<Result<TestRunResultDto>> RunAsync(
        Guid codeScriptId,
        ScriptTestRunRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Builds the per-execution <see cref="IScriptHostBridge"/> bound to one channel + trigger user (custom-code.md
/// §3.1). The single construction site for the fat host-dispatch bridge, shared by the live run orchestrator and
/// the capture-mode test-run so neither duplicates its wiring.
/// </summary>
public interface IScriptHostBridgeFactory
{
    IScriptHostBridge Create(Guid broadcasterId, string triggeringUserId);
}
