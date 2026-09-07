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

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Save-time, fail-closed validator. The capability-broker invariant lives here:
/// no action config may carry a url, secret, credential, or tenant-identifying value.
/// Also enforces: known action type, known condition type, step-count within cap,
/// and code-only CodeScriptId usage.
/// </summary>
public interface ICommandConfigValidator
{
    /// <summary>Validates a full pipeline graph before it is persisted.</summary>
    Task<Result<PipelineValidationResult>> ValidatePipelineAsync(
        PipelineGraphInput graph,
        CancellationToken ct = default
    );

    /// <summary>Validates a single action definition in isolation.</summary>
    Result<PipelineValidationResult> ValidateAction(ActionDefinition action);

    /// <summary>
    /// Returns <paramref name="action"/> with every <see cref="PipelineActionFieldKind.ResourceId"/> field's
    /// value normalized to its canonical raw-<see cref="Guid"/> string — decoding a ULID wire form when the
    /// caller sent one (the dashboard's resource pickers return the owned id in whichever form the API last
    /// served it in). Only call this on an action that already passed validation: a value that fails to decode
    /// is left untouched, since <see cref="ValidatePipelineAsync"/>/<see cref="ValidateAction"/> already reject
    /// that upstream.
    /// </summary>
    ActionDefinition NormalizeResourceIdFields(ActionDefinition action);
}

public sealed record PipelineGraphInput(IReadOnlyList<PipelineStepInput> Steps);

public sealed record PipelineStepInput(
    string ActionType,
    Dictionary<string, object?> Config,
    string? ConditionType = null,
    Dictionary<string, object?>? ConditionParams = null,
    bool IsEnabled = true
);

public sealed class PipelineValidationResult
{
    public static PipelineValidationResult Valid() => new() { IsValid = true };

    public static PipelineValidationResult Invalid(string reason, string code) =>
        new()
        {
            IsValid = false,
            ErrorCode = code,
            ErrorMessage = reason,
        };

    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
