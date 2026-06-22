// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using global::Jint;
using global::Jint.Runtime;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;

namespace NomNomzBot.Infrastructure.CustomCode.Jint;

/// <summary>
/// The self-host sandbox executor (custom-code.md §3.1, code-execution-sandbox.md §4.2). Runs compiled JS in a
/// fresh hardened Jint engine under the resource budget; the only guest surface is a primitive-in/primitive-out
/// <c>bot</c> facade whose side-effecting calls reach host code ONLY through the per-execution
/// <see cref="IScriptHostBridge"/>, capability-key-gated and host-call-budgeted. NEVER throws a sandbox escape
/// outward — every fault maps to the matching <see cref="ScriptExecutionOutcome"/> (fail-closed).
/// </summary>
public sealed class JintScriptExecutor : IScriptExecutor
{
    public ScriptRuntimeKind Runtime => ScriptRuntimeKind.Jint;

    // Builds the `bot` facade from the host primitives. Host-driven Execute (not guest eval), so it is allowed
    // under DisableStringCompilation. JSON.parse is a safe builtin (not code-from-string).
    private const string Bootstrap = """
        var bot = {
            args: JSON.parse(__argsJson),
            getVar: function (k) { return __getVar(String(k)); },
            setVar: function (k, v) { __setVar(String(k), String(v)); },
            send: function (m) { __send(String(m)); },
            call: function (k) {
                return __call(String(k), JSON.stringify(Array.prototype.slice.call(arguments, 1).map(String)));
            }
        };
        """;

    public Task<Result<ScriptCompilation>> CompileAsync(
        string sourceCode,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Parse only — never execute (no side effects, no host imports) — to reject syntax errors at save time.
            Engine.PrepareScript(sourceCode);
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Result.Failure<ScriptCompilation>(
                    $"Script failed to compile: {ex.Message}",
                    "VALIDATION_FAILED"
                )
            );
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCode)));
        return Task.FromResult(
            Result.Success(new ScriptCompilation(sourceCode, hash, Array.Empty<string>()))
        );
    }

    public Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, string> vars = new(request.Inputs.Variables, StringComparer.Ordinal);
        StringBuilder output = new();
        HashSet<string> grantedKeys = new(grant.Granted.Select(g => g.Key), StringComparer.Ordinal);
        int hostCalls = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        ScriptExecutionOutcome outcome;
        string? error = null;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromMilliseconds(request.Budget.WallClockMs));

        try
        {
            Engine engine = JintEngineFactory.CreateHardened(request.Budget, timeout.Token);

            engine.SetValue(
                "__getVar",
                (Func<string, string?>)(k => vars.TryGetValue(k, out string? v) ? v : null)
            );
            engine.SetValue(
                "__setVar",
                (Action<string, string>)((k, v) => vars[k] = v ?? string.Empty)
            );
            engine.SetValue(
                "__send",
                (Action<string>)(m => Append(output, m, request.Budget.MaxOutputBytes))
            );
            engine.SetValue(
                "__call",
                (Func<string, string, string?>)(
                    (key, argsJson) =>
                    {
                        if (!grantedKeys.Contains(key))
                            throw new ScriptCapabilityDeniedException(key);
                        if (++hostCalls > request.Budget.MaxHostCalls)
                            throw new ScriptHostBudgetException();
                        IReadOnlyList<string> a =
                            JsonConvert.DeserializeObject<List<string>>(argsJson) ?? [];
                        return bridge.Resolve(key)(key, a, timeout.Token);
                    }
                )
            );
            engine.SetValue("__argsJson", JsonConvert.SerializeObject(request.Inputs.Args));

            engine.Execute(Bootstrap);
            engine.Execute(request.CompiledJs);
            outcome = ScriptExecutionOutcome.Success;
        }
        catch (ScriptHostBudgetException)
        {
            outcome = ScriptExecutionOutcome.HostBudgetExceeded;
            error = "Host-call budget exceeded.";
        }
        catch (ScriptCapabilityDeniedException ex)
        {
            outcome = ScriptExecutionOutcome.Denied;
            error = $"Capability denied: {ex.CapabilityKey}.";
        }
        catch (Exception ex)
            when (ex is TimeoutException or ExecutionCanceledException or OperationCanceledException
            )
        {
            outcome = ScriptExecutionOutcome.Timeout;
            error = "Execution exceeded its time budget.";
        }
        catch (Exception ex)
            when (ex
                    is StatementsCountOverflowException
                        or MemoryLimitExceededException
                        or RecursionDepthOverflowException
            )
        {
            outcome = ScriptExecutionOutcome.Faulted;
            error = "Execution exceeded a resource limit.";
        }
        catch (JavaScriptException ex)
        {
            outcome = ScriptExecutionOutcome.Faulted;
            error = ex.Message;
        }
        catch (Exception)
        {
            // Fail-closed: never surface a sandbox escape / host exception to the caller.
            outcome = ScriptExecutionOutcome.Faulted;
            error = "Script execution faulted.";
        }
        stopwatch.Stop();

        return Task.FromResult(
            Result.Success(
                new ScriptExecutionOutcomeResult(
                    outcome,
                    stopwatch.ElapsedMilliseconds,
                    hostCalls,
                    vars,
                    output.Length == 0 ? null : output.ToString(),
                    StopPipeline: false,
                    error
                )
            )
        );
    }

    private static void Append(StringBuilder output, string message, long maxBytes)
    {
        if (output.Length >= maxBytes)
            return;
        int room = (int)Math.Min(maxBytes - output.Length, message.Length);
        output.Append(message, 0, room);
    }

    private sealed class ScriptHostBudgetException : Exception;

    private sealed class ScriptCapabilityDeniedException(string capabilityKey) : Exception
    {
        public string CapabilityKey { get; } = capabilityKey;
    }
}
