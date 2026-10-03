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
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.DevPlatform;
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
    IScriptHostBridgeFactory bridgeFactory,
    ITtsDispatchService ttsDispatch,
    ITriggerSampleCatalog samples
) : IScriptTestRunService
{
    public async Task<Result<TestRunResultDto>> RunAsync(
        Guid codeScriptId,
        ScriptTestRunRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not { } broadcasterId)
            return Result.Failure<TestRunResultDto>("No tenant.", "NO_TENANT");

        CodeScript? script = await db.CodeScripts.FirstOrDefaultAsync(
            s => s.Id == codeScriptId && s.BroadcasterId == broadcasterId && s.DeletedAt == null,
            cancellationToken
        );
        if (script is null)
            return Result.Failure<TestRunResultDto>("Script not found.", "NOT_FOUND");

        CodeScriptVersion? version = script.CurrentVersionId is { } versionId
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
                    Log: [$"Capability denied: {grant.ErrorMessage}"],
                    VariablesSet: new Dictionary<string, string>(),
                    Console: []
                )
            );

        string? roleToken = ResolveRoleToken(request.Role);
        if (!string.IsNullOrWhiteSpace(request.Role) && roleToken is null)
            return Result.Failure<TestRunResultDto>(
                $"Unknown role '{request.Role}'.",
                "VALIDATION_FAILED"
            );

        TriggerSample? sample = null;
        if (request.Trigger is not null)
        {
            sample = samples.Find(request.Trigger);
            if (sample is null)
                return Result.Failure<TestRunResultDto>(
                    $"Unknown trigger '{request.Trigger}'.",
                    "VALIDATION_FAILED"
                );
        }

        Dictionary<string, string> seeded = SeedVariables(sample, request, roleToken);
        string triggeringUserId = sample?.UserId ?? broadcasterId.ToString();
        CaptureSink sink = new();
        ScriptExecutionRequest execRequest = new(
            Guid.NewGuid().ToString("N")[..12],
            version.CompiledJs,
            version.CompiledHash ?? string.Empty,
            new(triggeringUserId, sample?.UserDisplayName ?? "Test Run", request.Args, seeded),
            ScriptResourceBudget.Baseline,
            sink.AddConsoleLine,
            sink.AddBotSend
        );

        IScriptHostBridge realBridge = bridgeFactory.Create(
            broadcasterId,
            triggeringUserId,
            replyTo: null
        );
        CaptureScriptHostBridge captureBridge = new(
            realBridge,
            sink,
            (voiceIdOverride, ct) =>
                ttsDispatch
                    .ResolveVoiceAsync(broadcasterId, triggeringUserId, voiceIdOverride, ct)
                    .GetAwaiter()
                    .GetResult()
        );

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
                log,
                ChangedVariables(seeded, outcome.VariablesOut),
                outcome.LogLines
            )
            {
                Timeline = sink.Timeline,
            }
        );
    }

    // The sample's variables first, then the author's edits over them, then the chosen viewer role.
    private static Dictionary<string, string> SeedVariables(
        TriggerSample? sample,
        ScriptTestRunRequest request,
        string? roleToken
    )
    {
        Dictionary<string, string> seeded = new(
            sample?.Variables ?? new Dictionary<string, string>(),
            StringComparer.Ordinal
        );
        if (sample?.ResponseKey == ChatCommandVariableKeys.Trigger && request.Args.Count > 0)
            foreach (
                KeyValuePair<string, string> pair in ChatMessageHandler.BuildArgumentVariables(
                    string.Join(' ', request.Args)
                )
            )
                seeded[pair.Key] = pair.Value;
        foreach (KeyValuePair<string, string> pair in request.Variables)
            seeded[pair.Key] = pair.Value;
        if (roleToken is not null)
            seeded["user.role"] = roleToken;
        return seeded;
    }

    // A real role token or alias ("mod") becomes its canonical token; anything else is null, never a silent viewer.
    private static string? ResolveRoleToken(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        PermissionLevel level = ChatRole.Parse(role);
        bool isViewer = role.Trim().ToLowerInvariant() is "viewer" or "everyone";
        return level == PermissionLevel.Everyone && !isViewer ? null : ChatRole.ToToken(level);
    }

    // Only what the script set to a new value — an input it left alone is not something it did.
    private static Dictionary<string, string> ChangedVariables(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after
    ) =>
        after
            .Where(pair => !before.TryGetValue(pair.Key, out string? old) || old != pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
