// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// <see cref="IPlatformDefaultRestoreService"/>. A restore writes through the SAME paths a platform publish
/// uses for one copy — <see cref="IPipelineService.UpdateAsync"/> for a pipeline (the editor's own validate
/// and persist) and the timer installer's <see cref="IPlatformTemplateInstaller.UpdateCopyAsync"/> for a timer
/// — inside one transaction, then re-stamps provenance so the row reads as untouched again.
/// </summary>
public sealed class PlatformDefaultRestoreService(
    IApplicationDbContext db,
    IUnitOfWork uow,
    IPipelineService pipelines,
    IEnumerable<IPlatformTemplateInstaller> installers
) : IPlatformDefaultRestoreService
{
    public async Task<Result<PlatformDefaultPreviewDto>> PreviewAsync(
        Guid broadcasterId,
        string kind,
        Guid rowId,
        CancellationToken ct = default
    )
    {
        Result<PlatformSource> source = await LoadSourceAsync(broadcasterId, kind, rowId, ct);
        if (source.IsFailure)
            return source.WithValue<PlatformDefaultPreviewDto>(null!);

        return kind == PlatformContentKinds.Pipeline
            ? Result.Success(await PreviewPipelineAsync(source.Value, broadcasterId, rowId, ct))
            : Result.Success(await PreviewTimerAsync(source.Value, broadcasterId, rowId, ct));
    }

    public async Task<Result<PlatformDefaultPreviewDto>> RestoreAsync(
        Guid broadcasterId,
        string kind,
        Guid rowId,
        CancellationToken ct = default
    )
    {
        Result<PlatformSource> source = await LoadSourceAsync(broadcasterId, kind, rowId, ct);
        if (source.IsFailure)
            return source.WithValue<PlatformDefaultPreviewDto>(null!);

        Result restored = await uow.ExecuteInTransactionAsync(
            token =>
                kind == PlatformContentKinds.Pipeline
                    ? RestorePipelineAsync(broadcasterId, rowId, source.Value, token)
                    : RestoreTimerAsync(broadcasterId, rowId, source.Value, token),
            ct,
            shouldCommit: result => result.IsSuccess
        );
        if (restored.IsFailure)
            return restored.WithValue<PlatformDefaultPreviewDto>(null!);

        return await PreviewAsync(broadcasterId, kind, rowId, ct);
    }

    // --- Source ------------------------------------------------------------------------------------------

    private sealed record PlatformSource(
        PlatformContentDefinition Definition,
        PlatformContentVersion Version,
        int? InstalledVersion,
        string? InstalledHash
    );

    private async Task<Result<PlatformSource>> LoadSourceAsync(
        Guid broadcasterId,
        string kind,
        Guid rowId,
        CancellationToken ct
    )
    {
        if (kind is not (PlatformContentKinds.Pipeline or PlatformContentKinds.Timer))
            return Result.Failure<PlatformSource>(
                $"Restoring a '{kind}' to its platform default is not supported.",
                "VALIDATION_FAILED"
            );

        (Guid? definitionId, int? installedVersion, string? installedHash, bool found) =
            kind == PlatformContentKinds.Pipeline
                ? await PipelineProvenanceAsync(broadcasterId, rowId, ct)
                : await TimerProvenanceAsync(broadcasterId, rowId, ct);

        if (!found)
            return Result.Failure<PlatformSource>($"That {kind} was not found.", "NOT_FOUND");
        if (definitionId is null)
            return Result.Failure<PlatformSource>(
                $"This {kind} was made by the channel, so it has no platform default to go back to.",
                "NOT_PLATFORM_CONTENT"
            );

        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(
                d => d.Id == definitionId.Value,
                ct
            );
        PlatformContentVersion? version = definition?.CurrentVersionId is { } versionId
            ? await db.PlatformContentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct)
            : null;
        if (definition is null || version is null)
            return Result.Failure<PlatformSource>(
                "The platform default for this item is no longer published.",
                "NOT_FOUND"
            );

        return Result.Success(
            new PlatformSource(definition, version, installedVersion, installedHash)
        );
    }

    private async Task<(Guid?, int?, string?, bool)> PipelineProvenanceAsync(
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    )
    {
        PipelineEntity? row = await FindPipelineAsync(broadcasterId, rowId, ct);
        return row is null
            ? (null, null, null, false)
            : (
                row.PlatformSourceDefinitionId,
                row.PlatformSourceVersion,
                row.PlatformSourceHash,
                true
            );
    }

    private async Task<(Guid?, int?, string?, bool)> TimerProvenanceAsync(
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    )
    {
        DomainTimer? row = await FindTimerAsync(broadcasterId, rowId, ct);
        return row is null
            ? (null, null, null, false)
            : (
                row.PlatformSourceDefinitionId,
                row.PlatformSourceVersion,
                row.PlatformSourceHash,
                true
            );
    }

    private Task<PipelineEntity?> FindPipelineAsync(
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    ) =>
        db.Pipelines.FirstOrDefaultAsync(
            p => p.Id == rowId && p.BroadcasterId == broadcasterId,
            ct
        );

    private Task<DomainTimer?> FindTimerAsync(
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    ) => db.Timers.FirstOrDefaultAsync(t => t.Id == rowId && t.BroadcasterId == broadcasterId, ct);

    // --- Pipeline ----------------------------------------------------------------------------------------

    /// <summary>
    /// A pipeline's consequence is told in steps: how many it has against how many the default has, or — when
    /// the count matches but the channel changed a step or its order — that the step settings go back.
    /// </summary>
    private async Task<PlatformDefaultPreviewDto> PreviewPipelineAsync(
        PlatformSource source,
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    )
    {
        PipelineEntity row = (await FindPipelineAsync(broadcasterId, rowId, ct))!;
        List<string> currentTypes = await db
            .PipelineSteps.Where(s => s.PipelineId == rowId && s.BroadcasterId == broadcasterId)
            .OrderBy(s => s.ParentStepId != null)
            .ThenBy(s => s.Order)
            .Select(s => s.ActionType)
            .ToListAsync(ct);
        List<string> defaultTypes = DefaultStepTypes(source.Version.PayloadJson);

        bool isEdited = source.InstalledHash != PlatformContentHash.ComputeHash(row.GraphJsonCache);
        List<PlatformDefaultChangeDto> changes = [];
        if (currentTypes.Count != defaultTypes.Count)
            changes.Add(new("steps", Text(currentTypes.Count), Text(defaultTypes.Count)));
        else if (isEdited || !currentTypes.SequenceEqual(defaultTypes))
            changes.Add(new("step_settings", Text(currentTypes.Count), Text(defaultTypes.Count)));

        return ToPreview(PlatformContentKinds.Pipeline, rowId, source, isEdited, changes);
    }

    /// <summary>The action type of every step in a wire-shape graph (<c>{ "steps": [ { "action": { "type"
    /// } } ] }</c>), in the graph's own order.</summary>
    private static List<string> DefaultStepTypes(string payloadJson)
    {
        using JsonDocument doc = JsonDocument.Parse(payloadJson);
        if (
            !doc.RootElement.TryGetProperty("steps", out JsonElement steps)
            || steps.ValueKind != JsonValueKind.Array
        )
            return [];

        List<string> types = [];
        foreach (JsonElement step in steps.EnumerateArray())
        {
            string type =
                step.TryGetProperty("action", out JsonElement action)
                && action.TryGetProperty("type", out JsonElement typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
            types.Add(type);
        }
        return types;
    }

    private async Task<Result> RestorePipelineAsync(
        Guid broadcasterId,
        Guid rowId,
        PlatformSource source,
        CancellationToken ct
    )
    {
        JsonElement graph = JsonSerializer.Deserialize<JsonElement>(source.Version.PayloadJson);
        Result<PipelineDto> updated = await pipelines.UpdateAsync(
            broadcasterId.ToString(),
            rowId,
            new UpdatePipelineDto { GraphJsonCache = graph },
            ct
        );
        if (updated.IsFailure)
            return updated;

        PipelineEntity row = (await FindPipelineAsync(broadcasterId, rowId, ct))!;
        row.PlatformSourceDefinitionId = source.Definition.Id;
        row.PlatformSourceVersion = source.Version.Version;
        // The row's OWN graph after the write, never the payload's hash: the editor path re-serializes the
        // graph, so only the row's own text reads back as untouched (same rule as the provenance backfill).
        row.PlatformSourceHash = PlatformContentHash.ComputeHash(row.GraphJsonCache);
        row.PlatformSourceSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // --- Timer -------------------------------------------------------------------------------------------

    /// <summary>A timer's consequence is field by field, compared through the same template shape the
    /// installer hashes, so "edited" here and "edited" in a platform publish always agree.</summary>
    private async Task<PlatformDefaultPreviewDto> PreviewTimerAsync(
        PlatformSource source,
        Guid broadcasterId,
        Guid rowId,
        CancellationToken ct
    )
    {
        DomainTimer row = (await FindTimerAsync(broadcasterId, rowId, ct))!;
        TimerTemplatePayload current = TimerTemplatePayload.FromEntity(row);
        Result<TimerTemplatePayload> parsed = PlatformTemplateJson.Parse<TimerTemplatePayload>(
            source.Version.PayloadJson
        );
        TimerTemplatePayload target = parsed.IsSuccess ? parsed.Value : current;

        List<PlatformDefaultChangeDto> changes = [];
        if (current.Name != target.Name.Trim())
            changes.Add(new("name", current.Name, target.Name.Trim()));
        if (!current.Messages.SequenceEqual(target.Messages))
            changes.Add(
                new(
                    "messages",
                    string.Join("\n", current.Messages),
                    string.Join("\n", target.Messages)
                )
            );
        if (current.IntervalMinutes != target.IntervalMinutes)
            changes.Add(
                new("interval", Text(current.IntervalMinutes), Text(target.IntervalMinutes))
            );
        if (current.MinChatActivity != target.MinChatActivity)
            changes.Add(
                new(
                    "min_chat_activity",
                    Text(current.MinChatActivity),
                    Text(target.MinChatActivity)
                )
            );
        if (current.IsEnabled != target.IsEnabled)
            changes.Add(new("enabled", Text(current.IsEnabled), Text(target.IsEnabled)));
        if (current.FireOnce != target.FireOnce)
            changes.Add(new("fire_once", Text(current.FireOnce), Text(target.FireOnce)));

        bool isEdited = source.InstalledHash != current.ComputeHash();
        return ToPreview(PlatformContentKinds.Timer, rowId, source, isEdited, changes);
    }

    private static PlatformDefaultPreviewDto ToPreview(
        string kind,
        Guid rowId,
        PlatformSource source,
        bool isEdited,
        List<PlatformDefaultChangeDto> changes
    ) =>
        new(
            kind,
            rowId,
            source.Definition.DisplayName,
            source.InstalledVersion,
            source.Version.Version,
            isEdited,
            changes
        );

    private Task<Result> RestoreTimerAsync(
        Guid broadcasterId,
        Guid rowId,
        PlatformSource source,
        CancellationToken ct
    )
    {
        IPlatformTemplateInstaller installer = installers.First(i =>
            i.Kind == PlatformContentKinds.Timer
        );
        return installer.UpdateCopyAsync(
            new PlatformTemplateCopyUpdate(
                rowId,
                broadcasterId,
                new PlatformTemplateSource(source.Definition.Id, source.Version.Version),
                source.Version.PayloadJson
            ),
            ct
        );
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(bool value) => value ? "true" : "false";
}
