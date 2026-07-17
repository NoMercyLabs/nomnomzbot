// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.PipelineActions;

/// <summary>
/// Shared plumbing for the OBS pipeline actions (obs-control.md §5): <c>{var}</c> resolution against
/// the execution context, the bool/double param readers <see cref="ActionDefinition"/> lacks, and
/// the uniform Result → ActionResult mapping (fail closed; the OBS outcome lands in
/// <c>ctx.Variables["obs.last_error"]</c> on failure so a pipeline can react).
/// </summary>
public abstract class ObsActionBase : ICommandAction
{
    protected ObsActionBase(IObsControlService obs)
    {
        Obs = obs;
    }

    protected IObsControlService Obs { get; }

    public abstract string ActionType { get; }

    public abstract Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    );

    /// <summary>Reads a string param, resolving a whole-value <c>{variable}</c> reference.</summary>
    protected static string? Param(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string key
    )
    {
        string? raw = action.GetString(key);
        if (raw is not null && raw.StartsWith('{') && raw.EndsWith('}'))
            ctx.Variables.TryGetValue(raw[1..^1], out raw);
        return raw;
    }

    /// <summary>A required string param — empty/missing fails the action.</summary>
    protected static bool TryRequire(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string key,
        out string value,
        out ActionResult failure
    )
    {
        string? raw = Param(ctx, action, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = string.Empty;
            failure = ActionResult.Failure($"'{key}' is required");
            return false;
        }
        value = raw.Trim();
        failure = ActionResult.Success();
        return true;
    }

    protected static bool GetBool(ActionDefinition action, string key, bool defaultValue = false)
    {
        if (action.Parameters is null || !action.Parameters.TryGetValue(key, out JsonElement elem))
            return defaultValue;
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(elem.GetString(), out bool parsed)
                ? parsed
                : defaultValue,
            _ => defaultValue,
        };
    }

    protected static double? GetDouble(ActionDefinition action, string key)
    {
        if (action.Parameters is null || !action.Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.String => double.TryParse(
                elem.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed
            )
                ? parsed
                : null,
            _ => null,
        };
    }

    protected static Application.Obs.Dtos.ObsToggle ParseToggle(string verb) =>
        verb switch
        {
            "start" => Application.Obs.Dtos.ObsToggle.Start,
            "stop" => Application.Obs.Dtos.ObsToggle.Stop,
            _ => Application.Obs.Dtos.ObsToggle.Toggle,
        };

    /// <summary>Maps the service outcome; a failure also lands in <c>obs.last_error</c> for the pipeline.</summary>
    protected static ActionResult Map(PipelineExecutionContext ctx, Result result, string success)
    {
        if (result.IsSuccess)
            return ActionResult.Success(success);
        ctx.Variables["obs.last_error"] = result.ErrorMessage ?? "OBS request failed";
        return ActionResult.Failure(result.ErrorMessage ?? "OBS request failed");
    }
}
