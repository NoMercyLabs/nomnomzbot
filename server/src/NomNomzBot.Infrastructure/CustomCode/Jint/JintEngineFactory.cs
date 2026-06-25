// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
        return new Engine(options =>
        {
            // Resource constraints (best-effort, between-statement; the OS worker is the real bound).
            options.MaxStatements((int)budget.MaxFuelOrStatements);
            options.TimeoutInterval(TimeSpan.FromMilliseconds(budget.WallClockMs));
            options.LimitMemory(budget.MaxMemoryBytes);
            options.CancellationToken(ct); // external kill switch (worker-driven)

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
