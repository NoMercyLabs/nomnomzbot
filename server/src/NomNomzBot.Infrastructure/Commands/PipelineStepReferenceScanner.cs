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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Reads the tenant's stored <c>PipelineStep.ConfigJson</c> blobs to count the steps that name a resource
/// which has no foreign key (sound clips, widgets). The scan is done in memory because the config is an
/// opaque JSON string in both supported providers — a JSON operator would be provider-specific and would
/// still not cover the template case. The query is tenant-scoped and projects only the four columns it reads.
/// </summary>
public sealed class PipelineStepReferenceScanner : IPipelineStepReferenceScanner
{
    private const int MaxSampleNames = 5;

    /// <summary>The action type whose references live in user code, not in config — never statically visible.</summary>
    private const string RunCodeActionType = "run_code";

    private readonly IApplicationDbContext _db;

    public PipelineStepReferenceScanner(IApplicationDbContext db)
    {
        _db = db;
    }

    private sealed record StepConfigRow(Guid PipelineId, string ActionType, string? ConfigJson);

    public async Task<Result<PipelineStepReferenceScan>> ScanAsync(
        Guid broadcasterId,
        IReadOnlyList<string> fieldNames,
        IReadOnlyList<string> tokens,
        ReferenceMatchMode matchMode = ReferenceMatchMode.Exact,
        CancellationToken ct = default
    )
    {
        if (fieldNames.Count == 0 && matchMode is not ReferenceMatchMode.ContainsAnyField)
            return Result<PipelineStepReferenceScan>.Failure(
                "A reference scan needs at least one config field to read.",
                "VALIDATION_FAILED"
            );

        List<string> wanted = [.. tokens.Where(token => !string.IsNullOrWhiteSpace(token))];
        if (wanted.Count == 0)
            return Result<PipelineStepReferenceScan>.Failure(
                "A reference scan needs at least one non-empty token.",
                "VALIDATION_FAILED"
            );

        List<StepConfigRow> steps = await _db
            .PipelineSteps.Where(step => step.BroadcasterId == broadcasterId)
            .Select(step => new StepConfigRow(step.PipelineId, step.ActionType, step.ConfigJson))
            .ToListAsync(ct);

        // A run_code step reaches sound clips and widgets through the scripting SDK at run time; nothing about
        // that reference is stored in config, so its presence alone makes any total a floor.
        bool unreadable = steps.Any(step =>
            string.Equals(step.ActionType, RunCodeActionType, StringComparison.OrdinalIgnoreCase)
        );

        int matchCount = 0;
        HashSet<Guid> matchedPipelineIds = [];

        foreach (StepConfigRow step in steps)
        {
            FieldReadout readout = ReadFields(step.ConfigJson, fieldNames, wanted, matchMode);
            if (readout.HasUnresolvableValue)
                unreadable = true;
            if (!readout.Matched)
                continue;

            matchCount++;
            matchedPipelineIds.Add(step.PipelineId);
        }

        List<string> pipelineNames = await PipelineNamesAsync(
            broadcasterId,
            matchedPipelineIds,
            ct
        );

        return Result<PipelineStepReferenceScan>.Success(
            new PipelineStepReferenceScan(matchCount, pipelineNames, unreadable)
        );
    }

    public async Task<Result<PipelineStepReferenceScan>> CountByActionTypesAsync(
        Guid broadcasterId,
        IReadOnlyList<string> actionTypes,
        CancellationToken ct = default
    )
    {
        if (actionTypes.Count == 0)
            return Result<PipelineStepReferenceScan>.Failure(
                "An action-type count needs at least one action type.",
                "VALIDATION_FAILED"
            );

        List<StepConfigRow> steps = await _db
            .PipelineSteps.Where(step => step.BroadcasterId == broadcasterId)
            .Select(step => new StepConfigRow(step.PipelineId, step.ActionType, step.ConfigJson))
            .ToListAsync(ct);

        HashSet<string> wanted = new(actionTypes, StringComparer.OrdinalIgnoreCase);
        List<StepConfigRow> matched = [.. steps.Where(step => wanted.Contains(step.ActionType))];

        // A code script can call the provider through the SDK; that call is invisible here, so any total the
        // tenant holds code scripts for is a floor rather than a complete answer.
        bool unreadable = steps.Any(step =>
            string.Equals(step.ActionType, RunCodeActionType, StringComparison.OrdinalIgnoreCase)
        );

        HashSet<Guid> pipelineIds = [.. matched.Select(step => step.PipelineId)];
        List<string> pipelineNames = await PipelineNamesAsync(broadcasterId, pipelineIds, ct);

        return Result<PipelineStepReferenceScan>.Success(
            new PipelineStepReferenceScan(matched.Count, pipelineNames, unreadable)
        );
    }

    private async Task<List<string>> PipelineNamesAsync(
        Guid broadcasterId,
        HashSet<Guid> pipelineIds,
        CancellationToken ct
    ) =>
        pipelineIds.Count == 0
            ? []
            : await _db
                .Pipelines.Where(pipeline =>
                    pipeline.BroadcasterId == broadcasterId && pipelineIds.Contains(pipeline.Id)
                )
                .OrderBy(pipeline => pipeline.Name)
                .Select(pipeline => pipeline.Name)
                .Take(MaxSampleNames)
                .ToListAsync(ct);

    private readonly record struct FieldReadout(bool Matched, bool HasUnresolvableValue);

    private static FieldReadout ReadFields(
        string? configJson,
        IReadOnlyList<string> fieldNames,
        IReadOnlyList<string> wanted,
        ReferenceMatchMode matchMode
    )
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new FieldReadout(false, false);

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Config we cannot read might name the resource; saying "no" here would understate the radius.
            return new FieldReadout(false, true);
        }

        if (root.ValueKind is not JsonValueKind.Object)
            return new FieldReadout(false, true);

        bool matched = false;
        bool unresolvable = false;

        if (matchMode is ReferenceMatchMode.ContainsAnyField)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Value.ValueKind is not JsonValueKind.String)
                {
                    // Nested objects and arrays are not walked; a reference could hide in one, so the total
                    // this scan produces is a floor rather than a complete answer.
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        unresolvable = true;
                    continue;
                }

                string? text = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (text.Contains("{{", StringComparison.Ordinal))
                    unresolvable = true;
                if (Matches(text.Trim(), wanted, ReferenceMatchMode.Contains))
                    matched = true;
            }

            return new FieldReadout(matched, unresolvable);
        }

        foreach (string fieldName in fieldNames)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement value))
                continue;
            if (value.ValueKind is not JsonValueKind.String)
            {
                unresolvable = true;
                continue;
            }

            string? raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            // A template placeholder is resolved from the event at run time; which resource it lands on is
            // genuinely unknown until then, so it counts as unseen rather than as a miss.
            if (raw.Contains("{{", StringComparison.Ordinal))
            {
                unresolvable = true;
                continue;
            }

            if (Matches(raw.Trim(), wanted, matchMode))
                matched = true;
        }

        return new FieldReadout(matched, unresolvable);
    }

    private static bool Matches(
        string value,
        IReadOnlyList<string> wanted,
        ReferenceMatchMode matchMode
    ) =>
        matchMode switch
        {
            ReferenceMatchMode.Contains => wanted.Any(token =>
                value.Contains(token, StringComparison.OrdinalIgnoreCase)
            ),
            _ => wanted.Any(token =>
                string.Equals(value, token, StringComparison.OrdinalIgnoreCase)
            ),
        };
}
