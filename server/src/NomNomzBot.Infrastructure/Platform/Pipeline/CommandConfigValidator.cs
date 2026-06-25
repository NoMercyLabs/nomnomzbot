// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Save-time, fail-closed validator. Enforces the capability-broker invariant:
/// no action config may carry raw url, secret, credential, or tenant data.
/// Unknown action types are rejected at save (consistency with fail-closed engine).
/// </summary>
public sealed class CommandConfigValidator : ICommandConfigValidator
{
    private static readonly HashSet<string> BannedConfigKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url",
        "secret",
        "webhook_url",
        "api_key",
        "token",
        "password",
        "credential",
        "authorization",
        "bearer",
    };

    // Patterns that indicate a value looks like a URL or credential (heuristic).
    private const string HttpScheme = "http";
    private const string BearerPrefix = "Bearer ";

    private readonly HashSet<string> _knownActionTypes;

    public CommandConfigValidator(IEnumerable<ICommandAction> actions)
    {
        _knownActionTypes = new HashSet<string>(
            actions.Select(a => a.ActionType),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public Task<Result<PipelineValidationResult>> ValidatePipelineAsync(
        PipelineGraphInput graph,
        CancellationToken ct = default
    )
    {
        if (graph.Steps.Count == 0)
            return Task.FromResult(
                Result.Success(
                    PipelineValidationResult.Invalid("Pipeline has no steps.", "EMPTY_PIPELINE")
                )
            );

        if (graph.Steps.Count > 100)
            return Task.FromResult(
                Result.Success(
                    PipelineValidationResult.Invalid(
                        $"Pipeline exceeds maximum step count (100); has {graph.Steps.Count}.",
                        "STEP_COUNT_EXCEEDED"
                    )
                )
            );

        foreach (PipelineStepInput step in graph.Steps)
        {
            // Check action type registration and key-level invariants from raw config.
            if (string.IsNullOrWhiteSpace(step.ActionType))
                return Task.FromResult(
                    Result.Success(
                        PipelineValidationResult.Invalid(
                            "A step has an empty action type.",
                            "MISSING_ACTION_TYPE"
                        )
                    )
                );

            if (!_knownActionTypes.Contains(step.ActionType))
                return Task.FromResult(
                    Result.Success(
                        PipelineValidationResult.Invalid(
                            $"Unknown action type '{step.ActionType}'.",
                            "UNKNOWN_ACTION_TYPE"
                        )
                    )
                );

            foreach (string key in step.Config.Keys)
            {
                if (BannedConfigKeys.Contains(key))
                    return Task.FromResult(
                        Result.Success(
                            PipelineValidationResult.Invalid(
                                $"Config key '{key}' is not allowed (broker-pattern invariant).",
                                "BANNED_CONFIG_KEY"
                            )
                        )
                    );
            }
        }

        return Task.FromResult(Result.Success(PipelineValidationResult.Valid()));
    }

    public Result<PipelineValidationResult> ValidateAction(ActionDefinition action)
    {
        if (string.IsNullOrWhiteSpace(action.Type))
            return Result.Success(
                PipelineValidationResult.Invalid(
                    "Action type must not be empty.",
                    "MISSING_ACTION_TYPE"
                )
            );

        if (!_knownActionTypes.Contains(action.Type))
            return Result.Success(
                PipelineValidationResult.Invalid(
                    $"Unknown action type '{action.Type}'. Register it as ICommandAction before using it in a pipeline.",
                    "UNKNOWN_ACTION_TYPE"
                )
            );

        if (action.Parameters is not null)
        {
            foreach (KeyValuePair<string, System.Text.Json.JsonElement> kv in action.Parameters)
            {
                if (BannedConfigKeys.Contains(kv.Key))
                    return Result.Success(
                        PipelineValidationResult.Invalid(
                            $"Config key '{kv.Key}' is not allowed in action configs (broker-pattern invariant).",
                            "BANNED_CONFIG_KEY"
                        )
                    );

                if (kv.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    string strVal = kv.Value.GetString() ?? string.Empty;
                    if (
                        strVal.StartsWith(HttpScheme, StringComparison.OrdinalIgnoreCase)
                        && strVal.Contains("://")
                    )
                        return Result.Success(
                            PipelineValidationResult.Invalid(
                                $"Action config value for '{kv.Key}' appears to be a URL, which is not allowed (broker-pattern invariant).",
                                "URL_IN_CONFIG"
                            )
                        );

                    if (strVal.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                        return Result.Success(
                            PipelineValidationResult.Invalid(
                                $"Action config value for '{kv.Key}' appears to be a credential, which is not allowed (broker-pattern invariant).",
                                "CREDENTIAL_IN_CONFIG"
                            )
                        );
                }
            }
        }

        return Result.Success(PipelineValidationResult.Valid());
    }
}
