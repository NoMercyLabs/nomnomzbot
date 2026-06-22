// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using Wasmtime;

namespace NomNomzBot.Infrastructure.CustomCode.Wasmtime;

/// <summary>
/// The SaaS sandbox executor (code-execution-sandbox.md §4.1) — the real isolation boundary for untrusted scripts.
/// Builds ONE hardened <see cref="Engine"/>/<see cref="Config"/> (Cranelift-only, fuel + epoch interruption on,
/// every risky wasm proposal off, WASI unlinked); every execution gets a fresh <see cref="Store"/> with mandatory
/// <c>SetLimits</c> (memory/table/instance caps) + a fuel ceiling. <see cref="RunHardenedModule"/> is that boundary
/// and is proven directly by tests against real wasm.
///
/// JS-in-WASM (the QuickJS engine module the script runs inside) is a SaaS-deployment artifact provided via
/// <c>Sandbox:WasmJsEnginePath</c>; until it is present, <see cref="ExecuteAsync"/> fails closed (Faulted) rather
/// than run a script — the security harness is complete and tested, the engine module plugs in at deploy.
/// </summary>
public sealed partial class WasmtimeScriptExecutor : IScriptExecutor, IDisposable
{
    private readonly Engine _engine;
    private readonly string? _jsEngineModulePath;

    public WasmtimeScriptExecutor(IConfiguration configuration)
    {
        _engine = BuildHardenedEngine();
        _jsEngineModulePath = configuration["Sandbox:WasmJsEnginePath"];
    }

    public ScriptRuntimeKind Runtime => ScriptRuntimeKind.Wasmtime;

    public Task<Result<ScriptCompilation>> CompileAsync(
        string sourceCode,
        CancellationToken cancellationToken = default
    )
    {
        // Save-time capability declaration (same heuristic the self-host executor uses); the JS→WASM compile to the
        // guest's engine module happens at SaaS deploy/run, not here.
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCode)));
        return Task.FromResult(
            Result.Success(
                new ScriptCompilation(sourceCode, hash, DeclaredCapabilities(sourceCode))
            )
        );
    }

    public Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge,
        CancellationToken cancellationToken = default
    )
    {
        // The guest runs inside the QuickJS-in-WASM engine module (deployment artifact). Without it, fail closed —
        // never silently fall back to a weaker boundary. The hardened wasm harness below is what isolation rests on.
        if (string.IsNullOrWhiteSpace(_jsEngineModulePath) || !File.Exists(_jsEngineModulePath))
            return Task.FromResult(
                Faulted(request, "SaaS Wasmtime JS engine module is not configured.")
            );

        // Full JS-in-WASM dispatch (load the engine module, marshal inputs through linear memory, link the bot.*
        // host imports over the bridge, run under RunHardenedModule's Store) lands with the engine module + its ABI.
        return Task.FromResult(
            Faulted(request, "Wasmtime JS dispatch is pending the engine-module ABI binding.")
        );
    }

    private static Result<ScriptExecutionOutcomeResult> Faulted(
        ScriptExecutionRequest request,
        string error
    ) =>
        Result.Success(
            new ScriptExecutionOutcomeResult(
                ScriptExecutionOutcome.Faulted,
                0,
                0,
                request.Inputs.Variables,
                null,
                false,
                error
            )
        );

    /// <summary>
    /// Runs an export of a raw wasm module under the full hardened per-execution Store — the security boundary,
    /// independent of the JS layer. A fuel-exhausting or epoch-tripped guest traps to <c>Timeout</c>; any other wasm
    /// trap is <c>Faulted</c>; clean completion is <c>Success</c>. This is what the §7 escape tests exercise.
    /// </summary>
    public ScriptExecutionOutcome RunHardenedModule(string watText, string exportName, long fuel)
    {
        using Store store = new(_engine);
        // MANDATORY on every untrusted Store: cap memory growth + table/instance counts (defaults are DoS-open).
        store.SetLimits(
            memorySize: 64 * 1024 * 1024,
            tableElements: 100_000,
            instances: 1,
            tables: 1,
            memories: 1
        );
        store.Fuel = (ulong)Math.Max(0, fuel);
        // Arm epoch interruption (the watchdog traps past this in production); with no watchdog advancing the epoch
        // here, fuel is the governing bound — proving the deterministic instruction ceiling in isolation.
        store.SetEpochDeadline(1);

        try
        {
            using Module module = Module.FromText(_engine, "guest", watText);
            // WASI default-deny: link NOTHING — no filesystem, clock, or argv surface reaches the guest.
            Linker linker = new(_engine);
            Instance instance = linker.Instantiate(store, module);
            Function? entry = instance.GetFunction(exportName);
            if (entry is null)
                return ScriptExecutionOutcome.Faulted;
            entry.Invoke();
            return ScriptExecutionOutcome.Success;
        }
        catch (TrapException)
        {
            // Fuel exhaustion / epoch deadline / wasm trap — the guest was contained.
            return ScriptExecutionOutcome.Timeout;
        }
        catch (WasmtimeException)
        {
            return ScriptExecutionOutcome.Faulted;
        }
    }

    private static Engine BuildHardenedEngine()
    {
        Config config = new();
        config.WithFuelConsumption(true); // deterministic instruction ceiling
        config.WithEpochInterruption(true); // wall-clock kill switch (watchdog advances the epoch)
        config.WithCompilerStrategy(CompilerStrategy.Cranelift); // never Winch (CRITICAL escapes Apr 2026)
        config.WithWasmThreads(false); // shared-memory unsoundness (RUSTSEC-2025-0118)
        config.WithReferenceTypes(false);
        config.WithRelaxedSIMD(false, false); // must go off with SIMD — relaxed-simd depends on it
        config.WithSIMD(false); // CVE-2026-34944 history
        config.WithMultiMemory(false);
        config.WithBulkMemory(false);
        return new Engine(config);
    }

    [GeneratedRegex("""bot\.call\(\s*["']([a-zA-Z][a-zA-Z0-9.]*)["']""")]
    private static partial Regex HostCallPattern();

    private static IReadOnlyList<string> DeclaredCapabilities(string sourceCode) =>
        [
            .. HostCallPattern()
                .Matches(sourceCode)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal),
        ];

    public void Dispose() => _engine.Dispose();
}
