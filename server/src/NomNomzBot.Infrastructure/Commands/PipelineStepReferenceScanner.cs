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
        CancellationToken ct = default
    )
    {
        if (fieldNames.Count == 0 || tokens.Count == 0)
            return Result<PipelineStepReferenceScan>.Failure(
                "A reference scan needs at least one config field and one token.",
                "VALIDATION_FAILED"
            );

        HashSet<string> wanted = new(tokens, StringComparer.OrdinalIgnoreCase);

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
            FieldReadout readout = ReadFields(step.ConfigJson, fieldNames, wanted);
            if (readout.HasUnresolvableValue)
                unreadable = true;
            if (!readout.Matched)
                continue;

            matchCount++;
            matchedPipelineIds.Add(step.PipelineId);
        }

        List<string> pipelineNames =
            matchedPipelineIds.Count == 0
                ? []
                : await _db
                    .Pipelines.Where(pipeline =>
                        pipeline.BroadcasterId == broadcasterId
                        && matchedPipelineIds.Contains(pipeline.Id)
                    )
                    .OrderBy(pipeline => pipeline.Name)
                    .Select(pipeline => pipeline.Name)
                    .Take(MaxSampleNames)
                    .ToListAsync(ct);

        return Result<PipelineStepReferenceScan>.Success(
            new PipelineStepReferenceScan(matchCount, pipelineNames, unreadable)
        );
    }

    private readonly record struct FieldReadout(bool Matched, bool HasUnresolvableValue);

    private static FieldReadout ReadFields(
        string? configJson,
        IReadOnlyList<string> fieldNames,
        HashSet<string> wanted
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

            if (wanted.Contains(raw.Trim()))
                matched = true;
        }

        return new FieldReadout(matched, unresolvable);
    }
}
