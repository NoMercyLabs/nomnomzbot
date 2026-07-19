// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.TestRun;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The code-script DRY-RUN (custom-code.md §6). Runs the script's current valid version through the REAL hardened
/// executor and the REAL host bridge — but wrapped in a <see cref="CaptureScriptHostBridge"/>, so reads run live and
/// every side-effecting capability is recorded, never dispatched. Unlike the live <see cref="ScriptRunner"/> it does
/// NOT gate on the sandbox meter, does NOT record usage, and does NOT touch the script row (LastRanAt / errors) — a
/// test-run leaves zero trace. A disallowed declared capability still denies the whole run (fail-closed), matching a
/// real run, so the author sees the same grant verdict they would get live.
/// </summary>
public sealed class ScriptTestRunService(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    IScriptExecutor executor,
    IScriptCapabilityBroker broker,
    IScriptHostBridgeFactory bridgeFactory
) : IScriptTestRunService
{
    public async Task<Result<TestRunResultDto>> RunAsync(
        Guid codeScriptId,
        ScriptTestRunRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not Guid broadcasterId)
            return Result.Failure<TestRunResultDto>("No tenant.", "NO_TENANT");

        CodeScript? script = await db.CodeScripts.FirstOrDefaultAsync(
            s => s.Id == codeScriptId && s.BroadcasterId == broadcasterId && s.DeletedAt == null,
            cancellationToken
        );
        if (script is null)
            return Result.Failure<TestRunResultDto>("Script not found.", "NOT_FOUND");

        CodeScriptVersion? version = script.CurrentVersionId is Guid versionId
            ? await db.CodeScriptVersions.FirstOrDefaultAsync(
                v => v.Id == versionId,
                cancellationToken
            )
            : null;
        if (version is null || version.ValidationStatus != "valid" || version.CompiledJs is null)
            return Result.Failure<TestRunResultDto>(
                "Script has no valid published version to test.",
                "VALIDATION_FAILED"
            );

        // Same deny-by-default grant the live run builds — a disallowed declared capability denies the test-run too,
        // so the author gets the honest verdict without any effect ever firing.
        List<string> declared =
            JsonConvert.DeserializeObject<List<string>>(version.DeclaredCapabilitiesJson) ?? [];
        Result<ScriptCapabilityGrant> grant = await broker.BuildGrantAsync(
            broadcasterId,
            declared,
            cancellationToken
        );
        if (grant.IsFailure)
            return Result.Success(
                new TestRunResultDto(
                    Success: false,
                    Error: grant.ErrorMessage,
                    DurationMs: 0,
                    HostCallCount: 0,
                    CapturedEffects: [],
                    ChatOutput: [],
                    Log: [$"Capability denied: {grant.ErrorMessage}"]
                )
            );

        ScriptExecutionRequest execRequest = new(
            Guid.NewGuid().ToString("N")[..12],
            version.CompiledJs,
            version.CompiledHash ?? string.Empty,
            new ScriptInputs(
                broadcasterId.ToString(),
                "Test Run",
                request.Args,
                new Dictionary<string, string>(request.Variables, StringComparer.Ordinal)
            ),
            ScriptResourceBudget.Baseline
        );

        CaptureSink sink = new();
        IScriptHostBridge realBridge = bridgeFactory.Create(
            broadcasterId,
            broadcasterId.ToString()
        );
        CaptureScriptHostBridge captureBridge = new(realBridge, sink);

        Result<ScriptExecutionOutcomeResult> executed = await executor.ExecuteAsync(
            execRequest,
            grant.Value,
            captureBridge,
            cancellationToken
        );
        ScriptExecutionOutcomeResult outcome = executed.Value;

        List<string> chatOutput = [.. sink.ChatOutput];
        // `bot.send(...)` writes to the script's direct output channel (not a capability) — surface it as chat too.
        if (!string.IsNullOrEmpty(outcome.ChatOutput))
            chatOutput.Insert(0, outcome.ChatOutput);

        bool success = outcome.Outcome == ScriptExecutionOutcome.Success;
        List<string> log =
        [
            $"Outcome: {outcome.Outcome}",
            $"{outcome.HostCallCount} host call(s), {sink.Effects.Count} captured effect(s).",
        ];
        if (!success && outcome.ErrorMessage is not null)
            log.Add($"Error: {outcome.ErrorMessage}");

        return Result.Success(
            new TestRunResultDto(
                success,
                success ? null : outcome.ErrorMessage,
                outcome.ElapsedMs,
                outcome.HostCallCount,
                sink.Effects,
                chatOutput,
                log
            )
        );
    }
}
