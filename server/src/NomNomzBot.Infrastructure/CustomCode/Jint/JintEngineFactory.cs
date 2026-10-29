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

using Jint;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode.Jint;

/// <summary>
/// The ONLY place an untrusted Jint <c>Engine</c> is constructed (code-execution-sandbox.md §4.2). Jint's defaults
/// are dangerous on three of four axes, so this hardened factory is mandatory; ad-hoc <c>new Engine()</c> for
/// untrusted input is banned. A FRESH engine is built per execution (never pooled across tenants/requests — prevents
/// prototype-pollution / leaked-global carryover). CLR interop is HARD OFF: <c>AllowClr</c> is never called and all
/// interop defaults (false) are kept, so the catastrophic <c>obj.GetType()</c> → reflection → RCE surface is closed.
/// </summary>
public static class JintEngineFactory
{
    public static Engine CreateHardened(ScriptResourceBudget budget, CancellationToken ct)
    {
        return new(options =>
        {
            // Resource constraints (best-effort, between-statement; the OS worker is the real bound).
            options.MaxStatements((int)budget.MaxFuelOrStatements);
            // The script's own run budget (WallClockMs) lives in ct: the executor's run clock cancels it, and
            // pauses it while a host call waits. This timer only backstops the whole run in wall time.
            options.TimeoutInterval(TimeSpan.FromMilliseconds(budget.MaxRunMs));
            options.LimitMemory(budget.MaxMemoryBytes);
            options.CancellationToken(ct); // the run budget + external kill switch

            // Constraint properties — dangerous defaults, set explicitly.
            options.Constraints.MaxRecursionDepth = 64; // default -1 = no check
            options.Constraints.MaxExecutionStackCount = 500; // default disabled = no native-stack guard
            options.Constraints.MaxArraySize = 10_000; // default uint.MaxValue
            options.Constraints.RegexTimeout = TimeSpan.FromSeconds(1); // default 10s

            // Kill code-from-string (default StringCompilationAllowed = true): no eval / new Function(code),
            // and the Generator/Async/AsyncGenerator constructors with it.
            options.DisableStringCompilation();
            options.Strict();

            // CLR INTEROP: HARD OFF. Never call AllowClr(). Keep ALL interop defaults (false):
            // Interop.Enabled = false, AllowGetType = false, AllowSystemReflection = false, no AllowClrWrite.
        });
    }
}
