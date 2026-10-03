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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The host side of <c>nnz.api.actions.invoke</c>: runs one pipeline action for a script through the SAME
/// owner-gated executor the widget invoke uses (<see cref="IOwnerActionService"/>). The gate key is one per
/// action type, <c>actions.invoke:&lt;type&gt;</c>, so a script is granted exactly the action types it names.
/// The guest always gets the widget result shape <c>{success, output, error, variables}</c>; a refusal or failure
/// is a result with <c>success=false</c> and also a typed <see cref="ScriptHostError"/>.
/// </summary>
public sealed class ScriptActionInvoker(Guid broadcasterId, IOwnerActionService ownerActions)
{
    public const string KeyPrefix = "actions.invoke:";

    private const string CallerName = "script";

    /// <summary>The result a test run returns: the call is recorded, nothing ran, so it reads as a success.</summary>
    public static string CapturedResultJson => ResultJson(true, null, null, null);

    /// <summary>The action type a gate key names, or null when the key is not an action gate key.</summary>
    public static string? ActionTypeOf(string capabilityKey) =>
        capabilityKey.StartsWith(KeyPrefix, StringComparison.Ordinal)
        && capabilityKey.Length > KeyPrefix.Length
            ? capabilityKey[KeyPrefix.Length..]
            : null;

    /// <summary>
    /// <paramref name="args"/> are the optional parameters object and the optional variables object, both JSON.
    /// </summary>
    public (string ResultJson, ScriptHostError? Error) Invoke(
        string actionType,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (!TryParseObject(args, 0, out Dictionary<string, JsonElement>? parameters))
            return Refuse(
                new(
                    ScriptHostErrorCodes.InvalidArgument,
                    "The action parameters must be an object."
                )
            );
        if (!TryParseObject(args, 1, out Dictionary<string, JsonElement>? rawVariables))
            return Refuse(
                new(ScriptHostErrorCodes.InvalidArgument, "The action variables must be an object.")
            );

        Dictionary<string, string> variables = (rawVariables ?? []).ToDictionary(
            v => v.Key,
            v =>
                v.Value.ValueKind == JsonValueKind.String
                    ? v.Value.GetString() ?? string.Empty
                    : v.Value.GetRawText()
        );

        Result<WidgetActionOutcome> run = ownerActions
            .RunAsync(
                new(
                    broadcasterId,
                    actionType,
                    parameters,
                    variables,
                    CallerName,
                    $"script-action:{broadcasterId}"
                ),
                ct
            )
            .GetAwaiter()
            .GetResult();
        if (!run.IsSuccess)
            return Refuse(ScriptHostError.FromResult(run));

        WidgetActionOutcome outcome = run.Value;
        string json = ResultJson(
            outcome.Succeeded,
            outcome.Output,
            outcome.Error,
            outcome.Variables
        );
        return (
            json,
            outcome.Succeeded
                ? null
                : new(
                    ScriptHostErrorCodes.UpstreamFailed,
                    outcome.Error ?? $"'{actionType}' failed."
                )
        );
    }

    private static (string ResultJson, ScriptHostError? Error) Refuse(ScriptHostError error) =>
        (ResultJson(false, null, error.Message, null), error);

    private static bool TryParseObject(
        IReadOnlyList<string> args,
        int index,
        out Dictionary<string, JsonElement>? parsed
    )
    {
        parsed = null;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            return true;
        try
        {
            using JsonDocument document = JsonDocument.Parse(args[index]);
            if (document.RootElement.ValueKind == JsonValueKind.Null)
                return true;
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            parsed = document
                .RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string ResultJson(
        bool success,
        string? output,
        string? error,
        IReadOnlyDictionary<string, string>? variables
    ) =>
        JsonConvert.SerializeObject(
            new
            {
                success,
                output,
                error,
                variables = variables ?? new Dictionary<string, string>(),
            }
        );
}
