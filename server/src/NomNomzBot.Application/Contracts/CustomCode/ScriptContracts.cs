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
using NomNomzBot.Domain.CustomCode.Enums;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>Which sandbox backend an executor adapter provides (custom-code.md §4).</summary>
public enum ScriptRuntimeKind
{
    Jint,
    Wasmtime,
}

/// <summary>The per-execution resource clamp (custom-code.md §4; defaults in code-execution-sandbox.md §5.1).</summary>
public sealed record ScriptResourceBudget(
    int WallClockMs,
    int MaxHostCalls,
    long MaxFuelOrStatements,
    long MaxMemoryBytes,
    long MaxOutputBytes,
    long MaxEgressBytes
)
{
    /// <summary>The safety baseline applied to every tenant (sandbox §5.1, self-host/Jint column).</summary>
    public static ScriptResourceBudget Baseline { get; } =
        new(
            WallClockMs: 2000,
            MaxHostCalls: 64,
            MaxFuelOrStatements: 200_000,
            MaxMemoryBytes: 64L * 1024 * 1024,
            MaxOutputBytes: 8 * 1024,
            MaxEgressBytes: 256L * 1024
        );
}

/// <summary>Value snapshot of the script's inputs — NO PII (internal ids only) (custom-code.md §4).</summary>
public sealed record ScriptInputs(
    string TriggeredByUserId,
    string TriggeredByDisplayName,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Variables
);

/// <summary>Built by the runner, passed to the executor — copied/owned values only (custom-code.md §4).</summary>
public sealed record ScriptExecutionRequest(
    string ExecutionId,
    string CompiledJs,
    string CompiledHash,
    ScriptInputs Inputs,
    ScriptResourceBudget Budget
);

/// <summary>The capability surface handed to the sandbox — descriptors only, zero delegates (custom-code.md §4).</summary>
public sealed record ScriptCapabilityGrant(
    Guid BroadcasterId,
    IReadOnlyList<ScriptCapabilityDescriptor> Granted
);

public sealed record ScriptCapabilityDescriptor(
    string Key,
    string FloorTier,
    string FeatureFlagKey,
    bool SideEffecting
);

/// <summary>The executor's value-typed result + accounting (custom-code.md §4).</summary>
public sealed record ScriptExecutionOutcomeResult(
    ScriptExecutionOutcome Outcome,
    long ElapsedMs,
    int HostCallCount,
    IReadOnlyDictionary<string, string> VariablesOut,
    string? ChatOutput,
    bool StopPipeline,
    string? ErrorMessage
);

/// <summary>The result of compiling+validating a script at save time (custom-code.md §4).</summary>
public sealed record ScriptCompilation(
    string CompiledJs,
    string CompiledHash,
    IReadOnlyList<string> DeclaredCapabilities
);

/// <summary>
/// The host trampoline a granted <c>bot.*</c> import binds to — primitive-in / primitive-out only
/// (custom-code.md §3.1). The token is the per-execution watchdog token.
/// </summary>
public delegate string? HostImportDelegate(
    string capabilityKey,
    IReadOnlyList<string> args,
    CancellationToken ct
);

/// <summary>
/// Resolves the host-side dispatch delegate for one granted capability key (custom-code.md §3.1). Host-side only;
/// the returned delegate never crosses into guest memory.
/// </summary>
public interface IScriptHostBridge
{
    HostImportDelegate Resolve(string capabilityKey);
}

/// <summary>
/// The single sandbox boundary (custom-code.md §3.1). Compiles/validates at save time and executes compiled JS
/// once under a grant + budget. NEVER throws sandbox escapes outward — a denial / exceeded budget / fault maps to
/// the matching <see cref="ScriptExecutionOutcome"/> (fail-closed).
/// </summary>
public interface IScriptExecutor
{
    ScriptRuntimeKind Runtime { get; }

    Task<Result<ScriptCompilation>> CompileAsync(
        string sourceCode,
        CancellationToken cancellationToken = default
    );

    Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge,
        CancellationToken cancellationToken = default
    );
}
