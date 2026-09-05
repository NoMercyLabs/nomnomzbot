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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// <see cref="IPlatformContentService"/> — this slice implements <c>Kind = "command"</c> (system commands,
/// backed by <see cref="ChannelBuiltinCommand"/>), <c>Kind = "widget"</c> (first-party overlay widgets,
/// backed by <see cref="Widget"/>) and <c>Kind = "pipeline"</c> (system pipelines, backed by
/// <see cref="PipelineEntity"/>); publish is refused with <c>VALIDATION_FAILED</c> for the remaining kind
/// (code scripts) since its tenant-side fan-out target does not exist yet (a separate follow-up slice per
/// platform-admin.md §6). A widget version's <c>PayloadJson</c> is a <see cref="WidgetContentPayload"/> (Vue
/// SFC source + default settings/subscriptions); publish compiles the source through
/// <see cref="IVueSfcCompiler"/> BEFORE anything is written — a widget that cannot compile is rejected at
/// publish time, never discovered by a viewer with a blank overlay. A pipeline version's <c>PayloadJson</c>
/// is the SAME wire-shape action graph <c>UpdatePipelineDto.GraphJsonCache</c> accepts (the tree editor's own
/// format, built by <c>PipelineGraphBuilder</c>) — publish never re-implements graph validation or step
/// persistence; it calls <see cref="IPipelineService.UpdateAsync"/> per affected tenant, the SAME entry point
/// the dashboard's own pipeline editor uses, so a system pipeline is edited, validated and persisted through
/// ONE machinery (platform-admin.md §2.2's "no second, worse pipeline editor"). Every public method
/// re-asserts the caller's Plane-C permission via <see cref="IPlatformIamService.AuthorizePlatformAsync"/> —
/// the one call that both decides AND audits (roles-permissions.md's single authorization funnel, mirrored
/// from <c>PlatformAdminService</c>). A <c>content:publish</c> fan-out additionally appends its OWN audit row
/// once the job completes, carrying <see cref="IamAuditLog.AffectedTenantCount"/>/
/// <see cref="IamAuditLog.PublishJobId"/> (§5) — those two fields are only known after the fan-out runs, so
/// they cannot ride the upfront gate-check row.
/// </summary>
public sealed class PlatformContentService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    IUnitOfWork uow,
    IVueSfcCompiler vueCompiler,
    IWidgetService widgetService,
    IPipelineService pipelineService
) : IPlatformContentService
{
    public async Task<Result<PagedList<PlatformContentDefinitionDto>>> ListDefinitionsAsync(
        Guid actingPrincipalId,
        string? kind,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentRead,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PagedList<PlatformContentDefinitionDto>>(null!);

        IQueryable<PlatformContentDefinition> query = db.PlatformContentDefinitions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(d => d.Kind == kind);

        int total = await query.CountAsync(ct);
        List<PlatformContentDefinition> rows = await query
            .OrderBy(d => d.Kind)
            .ThenBy(d => d.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        Dictionary<Guid, int> versionNumberByVersionId = await db
            .PlatformContentVersions.Where(v => rows.Select(r => r.CurrentVersionId).Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Version, ct);

        List<PlatformContentDefinitionDto> items =
        [
            .. rows.Select(r => ToDto(r, versionNumberByVersionId)),
        ];

        return Result.Success(
            new PagedList<PlatformContentDefinitionDto>(items, page, pageSize, total)
        );
    }

    public async Task<Result<PlatformContentDefinitionDetailDto>> GetDefinitionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentRead,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentDefinitionDetailDto>(null!);

        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (definition is null)
            return Result.Failure<PlatformContentDefinitionDetailDto>(
                "Content definition not found.",
                "NOT_FOUND"
            );

        List<PlatformContentVersion> versions = await db
            .PlatformContentVersions.Where(v => v.DefinitionId == definitionId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

        Dictionary<Guid, int> versionNumberById = versions.ToDictionary(v => v.Id, v => v.Version);

        return Result.Success(
            new PlatformContentDefinitionDetailDto(
                ToDto(definition, versionNumberById),
                [.. versions.Select(ToDto)]
            )
        );
    }

    public async Task<Result<PlatformContentDefinitionDto>> CreateDefinitionAsync(
        Guid actingPrincipalId,
        CreateContentDefinitionRequest request,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentAuthor,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentDefinitionDto>(null!);

        if (!PlatformContentKinds.IsKnown(request.Kind))
            return Result.Failure<PlatformContentDefinitionDto>(
                $"Unknown content kind '{request.Kind}'.",
                "VALIDATION_FAILED"
            );

        bool duplicate = await db.PlatformContentDefinitions.AnyAsync(
            d => d.Kind == request.Kind && d.Key == request.Key,
            ct
        );
        if (duplicate)
            return Result.Failure<PlatformContentDefinitionDto>(
                $"A '{request.Kind}' content definition with key '{request.Key}' already exists.",
                "ALREADY_EXISTS"
            );

        DateTime now = DateTime.UtcNow;
        PlatformContentDefinition definition = new()
        {
            Kind = request.Kind,
            Key = request.Key,
            DisplayName = request.DisplayName,
            Description = request.Description,
            CreatedAt = now,
            CreatedByPrincipalId = actingPrincipalId,
        };
        db.PlatformContentDefinitions.Add(definition);

        PlatformContentVersion version = new()
        {
            DefinitionId = definition.Id,
            Version = 1,
            ContentHash = PlatformContentHash.ComputeHash(request.PayloadJson),
            PayloadJson = request.PayloadJson,
            DraftedAt = now,
            DraftedByPrincipalId = actingPrincipalId,
        };
        db.PlatformContentVersions.Add(version);
        definition.LatestDraftVersionId = version.Id;

        await uow.SaveChangesAsync(ct);

        Dictionary<Guid, int> versionNumberById = new() { [version.Id] = version.Version };
        return Result.Success(ToDto(definition, versionNumberById));
    }

    public async Task<Result<PlatformContentVersionDto>> DraftVersionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        DraftContentVersionRequest request,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentAuthor,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentVersionDto>(null!);

        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (definition is null)
            return Result.Failure<PlatformContentVersionDto>(
                "Content definition not found.",
                "NOT_FOUND"
            );

        int nextVersion =
            1
                + await db
                    .PlatformContentVersions.Where(v => v.DefinitionId == definitionId)
                    .Select(v => (int?)v.Version)
                    .MaxAsync(ct)
            ?? 1;

        DateTime now = DateTime.UtcNow;
        PlatformContentVersion version = new()
        {
            DefinitionId = definitionId,
            Version = nextVersion,
            ContentHash = PlatformContentHash.ComputeHash(request.PayloadJson),
            PayloadJson = request.PayloadJson,
            RenderGalleryRefs = request.RenderGalleryRefs?.ToList() ?? [],
            DraftedAt = now,
            DraftedByPrincipalId = actingPrincipalId,
        };
        db.PlatformContentVersions.Add(version);
        definition.LatestDraftVersionId = version.Id;

        await uow.SaveChangesAsync(ct);

        return Result.Success(ToDto(version));
    }

    public async Task<Result<PlatformContentVersionDto>> GetVersionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentRead,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentVersionDto>(null!);

        PlatformContentVersion? version = await db.PlatformContentVersions.FirstOrDefaultAsync(
            v => v.Id == versionId && v.DefinitionId == definitionId,
            ct
        );
        if (version is null)
            return Result.Failure<PlatformContentVersionDto>(
                "Content version not found.",
                "NOT_FOUND"
            );

        return Result.Success(ToDto(version));
    }

    public async Task<Result<PublishPreviewDto>> PreviewPublishAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        string mode,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentAuthor,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PublishPreviewDto>(null!);

        Result<(PlatformContentDefinition Definition, PlatformContentVersion Version)> loaded =
            await LoadDefinitionAndVersionAsync(definitionId, versionId, mode, ct);
        if (loaded.IsFailure)
            return loaded.WithValue<PublishPreviewDto>(null!);

        (PlatformContentDefinition definition, PlatformContentVersion version) = loaded.Value;

        PublishSelection selection = await SelectTenantRowsAsync(definition, version, mode, ct);

        List<string> sampleNames = definition.Kind switch
        {
            PlatformContentKinds.Widget => await db
                .Widgets.Where(w => selection.AffectedRowIds.Contains(w.Id))
                .Join(db.Channels, w => w.BroadcasterId, c => c.Id, (w, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
            PlatformContentKinds.Pipeline => await db
                .Pipelines.Where(p => selection.AffectedRowIds.Contains(p.Id))
                .Join(db.Channels, p => p.BroadcasterId, c => c.Id, (p, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
            _ => await db
                .ChannelBuiltinCommands.Where(b => selection.AffectedRowIds.Contains(b.Id))
                .Join(db.Channels, b => b.BroadcasterId, c => c.Id, (b, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
        };

        return Result.Success(
            new PublishPreviewDto(
                selection.AffectedRowIds.Count,
                selection.SkippedCount,
                sampleNames
            )
        );
    }

    public async Task<Result<PlatformContentPublishJobDto>> PublishAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        PublishContentRequest request,
        CancellationToken ct = default
    )
    {
        string permission =
            request.Mode == PlatformContentPublishModes.Force
                ? IamPermissionKeys.ContentPublishForce
                : IamPermissionKeys.ContentPublish;

        if (
            request.Mode == PlatformContentPublishModes.Force
            && string.IsNullOrWhiteSpace(request.PublishNote)
        )
            return Result.Failure<PlatformContentPublishJobDto>(
                "A publish note is required to justify a force publish.",
                "VALIDATION_FAILED"
            );

        Result gate = await RequireAsync(
            actingPrincipalId,
            permission,
            null,
            ct,
            justification: request.Mode == PlatformContentPublishModes.Force
                ? request.PublishNote
                : null,
            breakGlass: request.Mode == PlatformContentPublishModes.Force
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentPublishJobDto>(null!);

        Result<(PlatformContentDefinition Definition, PlatformContentVersion Version)> loaded =
            await LoadDefinitionAndVersionAsync(definitionId, versionId, request.Mode, ct);
        if (loaded.IsFailure)
            return loaded.WithValue<PlatformContentPublishJobDto>(null!);

        (PlatformContentDefinition definition, PlatformContentVersion version) = loaded.Value;

        if (definition.Kind == PlatformContentKinds.Widget)
        {
            Result compileGate = ValidateWidgetPayloadCompiles(version.PayloadJson);
            if (compileGate.IsFailure)
                return compileGate.WithValue<PlatformContentPublishJobDto>(null!);
        }

        if (definition.Kind == PlatformContentKinds.Pipeline)
        {
            Result parseGate = ValidatePipelinePayloadIsJson(version.PayloadJson);
            if (parseGate.IsFailure)
                return parseGate.WithValue<PlatformContentPublishJobDto>(null!);
        }

        PublishSelection freshSelection = await SelectTenantRowsAsync(
            definition,
            version,
            request.Mode,
            ct
        );
        if (freshSelection.AffectedRowIds.Count != request.ConfirmedPreviewAffectedCount)
            return Result.Failure<PlatformContentPublishJobDto>(
                "The affected-tenant count changed since the last preview. Run publish-preview again.",
                "PREVIEW_STALE"
            );

        DateTime now = DateTime.UtcNow;
        PlatformContentPublishJob job = new()
        {
            DefinitionId = definitionId,
            FromVersion = GetPublishedFromVersion(version),
            ToVersion = version.Version,
            Mode = request.Mode,
            RequestedByPrincipalId = actingPrincipalId,
            RequestedAt = now,
            PreviewAffectedCount = freshSelection.AffectedRowIds.Count,
            PreviewSkippedCount = freshSelection.SkippedCount,
            Status = PlatformContentPublishJobStatuses.Running,
        };
        db.PlatformContentPublishJobs.Add(job);

        IamOutcome outcome = IamOutcome.Allowed;
        string? failureReason = null;
        int confirmedCount = 0;

        try
        {
            if (request.Mode != PlatformContentPublishModes.PublishAsNew)
            {
                if (definition.Kind == PlatformContentKinds.Widget)
                {
                    WidgetFanOutResult widgetResult = await ApplyWidgetFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        now,
                        ct
                    );
                    confirmedCount = widgetResult.Count;
                    job.RebuildFailedWidgetIds = widgetResult.RebuildFailedWidgetIds;
                }
                else if (definition.Kind == PlatformContentKinds.Pipeline)
                {
                    PipelineFanOutResult pipelineResult = await ApplyPipelineFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        now,
                        ct
                    );
                    confirmedCount = pipelineResult.Count;
                    job.ValidationFailedPipelineIds = pipelineResult.ValidationFailedPipelineIds;
                }
                else
                {
                    confirmedCount = await ApplyCommandFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        now,
                        ct
                    );
                }
            }

            version.PublishedAt = version.PublishedAt ?? now;
            version.PublishedByPrincipalId = version.PublishedByPrincipalId ?? actingPrincipalId;
            version.PublishNote = request.PublishNote ?? version.PublishNote;
            definition.CurrentVersionId = version.Id;
            definition.LatestDraftVersionId = version.Id;

            job.ConfirmedAffectedCount = confirmedCount;
            job.Status = PlatformContentPublishJobStatuses.Completed;
            job.CompletedAt = now;

            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            outcome = IamOutcome.Failed;
            failureReason = ex.Message;
            job.Status = PlatformContentPublishJobStatuses.Failed;
            job.FailureReason = failureReason;
            job.CompletedAt = now;
            job.ConfirmedAffectedCount = confirmedCount;
            await uow.SaveChangesAsync(ct);
        }

        await AuditPublishOutcomeAsync(
            actingPrincipalId,
            permission,
            definition,
            version,
            job,
            outcome,
            request.PublishNote,
            ct
        );

        return outcome == IamOutcome.Failed
            ? Result.Failure<PlatformContentPublishJobDto>(
                failureReason ?? "Publish failed.",
                "INTERNAL_ERROR"
            )
            : Result.Success(ToDto(job));
    }

    public async Task<Result<PlatformContentPublishJobDto>> GetPublishJobAsync(
        Guid actingPrincipalId,
        Guid publishJobId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentRead,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformContentPublishJobDto>(null!);

        PlatformContentPublishJob? job = await db.PlatformContentPublishJobs.FirstOrDefaultAsync(
            j => j.Id == publishJobId,
            ct
        );
        if (job is null)
            return Result.Failure<PlatformContentPublishJobDto>(
                "Publish job not found.",
                "NOT_FOUND"
            );

        return Result.Success(ToDto(job));
    }

    public async Task<Result> RetireDefinitionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentAuthor,
            null,
            ct
        );
        if (gate.IsFailure)
            return gate;

        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (definition is null)
            return Result.Failure("Content definition not found.", "NOT_FOUND");

        definition.RetiredAt = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    // --- Compile gate (widget kind) -------------------------------------------------------------------

    /// <summary>Compiles the widget payload's Vue SFC source through <see cref="IVueSfcCompiler"/> — a
    /// stateless, DB-free check that runs BEFORE any tenant row is touched. A malformed payload or a source
    /// that fails to compile fails the whole publish with <c>VALIDATION_FAILED</c>.</summary>
    private Result ValidateWidgetPayloadCompiles(string payloadJson)
    {
        if (
            !WidgetContentPayload.TryParse(
                payloadJson,
                out WidgetContentPayload? payload,
                out string? error
            )
        )
            return Result.Failure(error!, "VALIDATION_FAILED");

        Result<VueSfcOutput> compiled = vueCompiler.Compile(payload!.SourceCode, "widget.vue");
        return compiled.IsFailure
            ? Result.Failure(
                $"Widget source failed to compile: {compiled.ErrorMessage}",
                "VALIDATION_FAILED"
            )
            : Result.Success();
    }

    /// <summary>Pipeline kind's publish gate: the payload must at least be well-formed JSON before the
    /// fan-out starts — a garbled draft never gets a chance to reach a single tenant row. The GRAPH itself
    /// (action/condition types, step wiring) is deliberately NOT validated here: that validation runs once
    /// PER TENANT inside <see cref="ApplyPipelineFanOutAsync"/> via <see cref="IPipelineService.UpdateAsync"/>
    /// — the same gate the dashboard's own editor goes through — so this method stays a cheap parse check,
    /// never a second, competing implementation of pipeline graph validation.</summary>
    private static Result ValidatePipelinePayloadIsJson(string payloadJson)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(payloadJson);
            return Result.Success();
        }
        catch (JsonException ex)
        {
            return Result.Failure(
                $"Pipeline content payload is not valid JSON: {ex.Message}",
                "VALIDATION_FAILED"
            );
        }
    }

    // --- Selection / fan-out -------------------------------------------------------------------------

    private readonly record struct PublishSelection(List<Guid> AffectedRowIds, int SkippedCount);

    /// <summary>
    /// Runs the EXACT SAME selection query publish uses (§2.1's "the preview runs the same query the
    /// publish will use" guarantee) against REAL <see cref="ChannelBuiltinCommand"/> rows, computed BEFORE
    /// anything is written.
    /// </summary>
    private async Task<PublishSelection> SelectTenantRowsAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        string mode,
        CancellationToken ct
    )
    {
        return definition.Kind switch
        {
            PlatformContentKinds.Command => await SelectCommandRowsAsync(definition, mode, ct),
            PlatformContentKinds.Widget => await SelectWidgetRowsAsync(definition, mode, ct),
            PlatformContentKinds.Pipeline => await SelectPipelineRowsAsync(definition, mode, ct),
            _ => new PublishSelection([], 0),
        };
    }

    private async Task<PublishSelection> SelectCommandRowsAsync(
        PlatformContentDefinition definition,
        string mode,
        CancellationToken ct
    )
    {
        List<ChannelBuiltinCommand> installed = await db
            .ChannelBuiltinCommands.Where(b => b.BuiltinKey == definition.Key)
            .ToListAsync(ct);

        switch (mode)
        {
            case PlatformContentPublishModes.PublishAsNew:
                // Zero blast radius by construction — nothing tenant-facing is touched.
                return new PublishSelection([], 0);

            case PlatformContentPublishModes.Force:
                return new PublishSelection([.. installed.Select(b => b.Id)], 0);

            case PlatformContentPublishModes.UpdateInPlaceWhereUntouched:
            default:
                List<Guid> untouched = [];
                int skipped = 0;
                foreach (ChannelBuiltinCommand row in installed)
                {
                    string liveHash = PlatformContentHash.ComputeHash(row.OverridesJson);
                    // A row never stamped with provenance (pre-backfill / not from this definition) is
                    // treated as tenant-authored and skipped — never matched by name (§2.1's guardrail).
                    if (row.PlatformSourceDefinitionId != definition.Id)
                    {
                        skipped++;
                        continue;
                    }
                    if (row.PlatformSourceHash == liveHash)
                        untouched.Add(row.Id);
                    else
                        skipped++;
                }
                return new PublishSelection(untouched, skipped);
        }
    }

    /// <summary>
    /// The "installed" set for a widget definition is every tenant <see cref="Widget"/> row already stamped
    /// with THIS <see cref="PlatformContentDefinition.Id"/> — a widget is opt-in per tenant (created by
    /// install-from-gallery or an earlier publish), unlike a builtin command's one-row-per-channel shape, so
    /// there is no name-derived candidate set to fall back to (the seeder principle's "never match by name"
    /// guardrail applies here too).
    /// </summary>
    private async Task<PublishSelection> SelectWidgetRowsAsync(
        PlatformContentDefinition definition,
        string mode,
        CancellationToken ct
    )
    {
        List<Widget> installed = await db
            .Widgets.Where(w => w.PlatformSourceDefinitionId == definition.Id)
            .ToListAsync(ct);

        switch (mode)
        {
            case PlatformContentPublishModes.PublishAsNew:
                return new PublishSelection([], 0);

            case PlatformContentPublishModes.Force:
                return new PublishSelection([.. installed.Select(w => w.Id)], 0);

            case PlatformContentPublishModes.UpdateInPlaceWhereUntouched:
            default:
                List<Guid> untouched = [];
                int skipped = 0;
                foreach (Widget row in installed)
                {
                    string liveHash = WidgetContentPayload.ComputeSettingsHash(
                        row.Settings,
                        row.EventSubscriptions
                    );
                    if (row.PlatformSourceHash == liveHash)
                        untouched.Add(row.Id);
                    else
                        skipped++;
                }
                return new PublishSelection(untouched, skipped);
        }
    }

    /// <summary>
    /// The "installed" set for a pipeline definition is every tenant <see cref="PipelineEntity"/> row
    /// already stamped with THIS <see cref="PlatformContentDefinition.Id"/> — seeded by
    /// <c>RaidFlowSeeder</c>/<c>RaidStartFlowSeeder</c>/<c>RaidCommitFlowSeeder</c> (or a later install), same
    /// opt-in-per-tenant shape as the widget kind (no name-derived candidate set to fall back to; the
    /// seeder principle's "never match by name" guardrail applies here too).
    /// </summary>
    private async Task<PublishSelection> SelectPipelineRowsAsync(
        PlatformContentDefinition definition,
        string mode,
        CancellationToken ct
    )
    {
        List<PipelineEntity> installed = await db
            .Pipelines.Where(p => p.PlatformSourceDefinitionId == definition.Id)
            .ToListAsync(ct);

        switch (mode)
        {
            case PlatformContentPublishModes.PublishAsNew:
                return new PublishSelection([], 0);

            case PlatformContentPublishModes.Force:
                return new PublishSelection([.. installed.Select(p => p.Id)], 0);

            case PlatformContentPublishModes.UpdateInPlaceWhereUntouched:
            default:
                List<Guid> untouched = [];
                int skipped = 0;
                foreach (PipelineEntity row in installed)
                {
                    string liveHash = PlatformContentHash.ComputeHash(row.GraphJsonCache);
                    if (row.PlatformSourceHash == liveHash)
                        untouched.Add(row.Id);
                    else
                        skipped++;
                }
                return new PublishSelection(untouched, skipped);
        }
    }

    private async Task<int> ApplyCommandFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        DateTime now,
        CancellationToken ct
    )
    {
        List<ChannelBuiltinCommand> targets = await db
            .ChannelBuiltinCommands.Where(b => affectedRowIds.Contains(b.Id))
            .ToListAsync(ct);

        foreach (ChannelBuiltinCommand row in targets)
        {
            row.OverridesJson = version.PayloadJson == "{}" ? null : version.PayloadJson;
            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = version.Version;
            row.PlatformSourceHash = version.ContentHash;
            row.PlatformSourceSyncedAt = now;
        }
        return targets.Count;
    }

    private readonly record struct WidgetFanOutResult(int Count, List<Guid> RebuildFailedWidgetIds);

    /// <summary>
    /// Writes the version's default settings/subscriptions onto every affected tenant <see cref="Widget"/> row,
    /// stamps provenance, and rebuilds each tenant's compiled bundle through the SAME
    /// <see cref="IWidgetService.CompileAsync"/> machinery a streamer's own compile-on-save uses — the fan-out
    /// no longer stops at "Settings changed but the viewer's overlay keeps rendering the stale bundle"
    /// (S-ADMIN-2c-b). Failure policy: a tenant whose rebuild fails keeps its PREVIOUS successful
    /// <c>WidgetVersion</c>/bundle live (<c>CompileAsync</c> only repoints <c>ActiveVersionId</c> on a
    /// successful build, never on error) and is recorded in the returned
    /// <see cref="WidgetFanOutResult.RebuildFailedWidgetIds"/> for
    /// <see cref="PlatformContentPublishJob.RebuildFailedWidgetIds"/> — never silently swallowed. A customised
    /// tenant was already excluded upstream by <see cref="SelectWidgetRowsAsync"/> and never reaches this loop.
    /// </summary>
    private async Task<WidgetFanOutResult> ApplyWidgetFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        DateTime now,
        CancellationToken ct
    )
    {
        if (
            !WidgetContentPayload.TryParse(
                version.PayloadJson,
                out WidgetContentPayload? payload,
                out _
            )
        )
            return new WidgetFanOutResult(0, []);

        List<Widget> targets = await db
            .Widgets.Where(w => affectedRowIds.Contains(w.Id))
            .ToListAsync(ct);

        string settingsHash = payload!.ComputeSettingsHash();
        List<Guid> rebuildFailedWidgetIds = [];
        foreach (Widget row in targets)
        {
            row.Settings = new Dictionary<string, object>(payload.DefaultSettings);
            row.EventSubscriptions = [.. payload.DefaultEventSubscriptions];
            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = version.Version;
            row.PlatformSourceHash = settingsHash;
            row.PlatformSourceSyncedAt = now;

            // Persist the settings/provenance write before rebuilding — CompileAsync loads its own tracked
            // copy of the row from the same DbContext, so the settings must already be visible to it.
            await uow.SaveChangesAsync(ct);

            bool rebuilt = await TryRebuildTenantBundleAsync(row, payload.SourceCode, ct);
            if (!rebuilt)
                rebuildFailedWidgetIds.Add(row.Id);
        }
        return new WidgetFanOutResult(targets.Count, rebuildFailedWidgetIds);
    }

    /// <summary>
    /// One tenant's rebuild attempt. Never throws out of this method — an exception from the compiler/build
    /// pipeline is caught and treated as a rebuild failure (recorded, not swallowed) exactly like a normal
    /// build-error result, so one tenant's bad luck never aborts the rest of the fan-out.
    /// </summary>
    private async Task<bool> TryRebuildTenantBundleAsync(
        Widget row,
        string sourceCode,
        CancellationToken ct
    )
    {
        try
        {
            Result<WidgetVersionDetail> compiled = await widgetService.CompileAsync(
                row.BroadcasterId.ToString(),
                row.Id.ToString(),
                new CompileWidgetRequest { SourceCode = sourceCode },
                ct
            );
            return compiled is { IsSuccess: true, Value.BuildStatus: "success" };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private readonly record struct PipelineFanOutResult(
        int Count,
        List<Guid> ValidationFailedPipelineIds
    );

    /// <summary>
    /// Runs the version's graph through <see cref="IPipelineService.UpdateAsync"/> for every affected tenant
    /// <see cref="PipelineEntity"/> — the SAME create/validate/persist path the dashboard's own pipeline
    /// tree editor uses (platform-admin.md §2.2), never a bespoke re-implementation. <c>UpdateAsync</c>
    /// validates the graph through <c>ICommandConfigValidator</c> BEFORE touching the row's
    /// <c>GraphJsonCache</c>/<c>PipelineStep</c> rows (<c>PipelineService.ValidateAndSerializeGraphAsync</c>
    /// runs, and only on success does the entity get mutated and saved) — so a tenant whose graph fails
    /// validation is left with its exact PREVIOUS working graph untouched, and is recorded in
    /// <see cref="PipelineFanOutResult.ValidationFailedPipelineIds"/> rather than silently skipped. Only a
    /// tenant whose update actually succeeded gets its provenance (<see cref="PipelineEntity.PlatformSourceDefinitionId"/>/
    /// <c>Version</c>/<c>Hash</c>/<c>SyncedAt</c>) stamped — a failed tenant's stored provenance hash is left
    /// exactly as it was, so it is offered again on the next publish rather than being mistaken for
    /// "already on the new version". One tenant's failure never aborts the rest of the fan-out.
    /// </summary>
    private async Task<PipelineFanOutResult> ApplyPipelineFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        DateTime now,
        CancellationToken ct
    )
    {
        JsonElement graph = JsonSerializer.Deserialize<JsonElement>(version.PayloadJson);

        List<PipelineEntity> targets = await db
            .Pipelines.Where(p => affectedRowIds.Contains(p.Id))
            .ToListAsync(ct);

        List<Guid> validationFailedPipelineIds = [];
        int appliedCount = 0;
        foreach (PipelineEntity row in targets)
        {
            Result<PipelineDto> updateResult = await pipelineService.UpdateAsync(
                row.BroadcasterId.ToString(),
                row.Id,
                new UpdatePipelineDto { GraphJsonCache = graph },
                ct
            );

            if (updateResult.IsFailure)
            {
                validationFailedPipelineIds.Add(row.Id);
                continue;
            }

            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = version.Version;
            row.PlatformSourceHash = version.ContentHash;
            row.PlatformSourceSyncedAt = now;
            appliedCount++;
        }

        return new PipelineFanOutResult(appliedCount, validationFailedPipelineIds);
    }

    private async Task AuditPublishOutcomeAsync(
        Guid actingPrincipalId,
        string permission,
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        PlatformContentPublishJob job,
        IamOutcome outcome,
        string? justification,
        CancellationToken ct
    )
    {
        db.IamAuditLogs.Add(
            new()
            {
                PrincipalId = actingPrincipalId,
                PrincipalType = IamPrincipalType.Employee,
                Permission = permission,
                TargetResource = $"{definition.Kind}:{definition.Key}@v{version.Version}",
                Justification = justification,
                BreakGlass = permission == IamPermissionKeys.ContentPublishForce,
                Outcome =
                    job.Status == PlatformContentPublishJobStatuses.Failed
                        ? IamOutcome.Partial
                        : outcome,
                OccurredAt = DateTime.UtcNow,
                AffectedTenantCount = job.ConfirmedAffectedCount,
                PublishJobId = job.Id,
            }
        );
        await uow.SaveChangesAsync(ct);
    }

    private async Task<
        Result<(PlatformContentDefinition Definition, PlatformContentVersion Version)>
    > LoadDefinitionAndVersionAsync(
        Guid definitionId,
        Guid versionId,
        string mode,
        CancellationToken ct
    )
    {
        if (!PlatformContentPublishModes.IsKnown(mode))
            return Result.Failure<(PlatformContentDefinition, PlatformContentVersion)>(
                $"Unknown publish mode '{mode}'.",
                "VALIDATION_FAILED"
            );

        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (definition is null)
            return Result.Failure<(PlatformContentDefinition, PlatformContentVersion)>(
                "Content definition not found.",
                "NOT_FOUND"
            );

        if (
            definition.Kind
            is not (
                PlatformContentKinds.Command
                or PlatformContentKinds.Widget
                or PlatformContentKinds.Pipeline
            )
        )
            return Result.Failure<(PlatformContentDefinition, PlatformContentVersion)>(
                $"Publishing kind '{definition.Kind}' is not supported yet.",
                "VALIDATION_FAILED"
            );

        PlatformContentVersion? version = await db.PlatformContentVersions.FirstOrDefaultAsync(
            v => v.Id == versionId && v.DefinitionId == definitionId,
            ct
        );
        if (version is null)
            return Result.Failure<(PlatformContentDefinition, PlatformContentVersion)>(
                "Content version not found.",
                "NOT_FOUND"
            );

        return Result.Success((definition, version));
    }

    private static int? GetPublishedFromVersion(PlatformContentVersion targetVersion) =>
        targetVersion.Version > 1 ? targetVersion.Version - 1 : null;

    // --- Authorization ---------------------------------------------------------------------------------

    /// <summary>The one authorization funnel — <see cref="IPlatformIamService.AuthorizePlatformAsync"/> both
    /// decides AND audits (allowed or denied) on SaaS; a denial maps to <c>FORBIDDEN</c> here. Mirrors
    /// <c>PlatformAdminService.RequireAsync</c>.</summary>
    private async Task<Result> RequireAsync(
        Guid principalId,
        string permissionKey,
        Guid? targetBroadcasterId,
        CancellationToken ct,
        string? justification = null,
        bool breakGlass = false
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            principalId,
            permissionKey,
            targetBroadcasterId,
            breakGlass,
            justification,
            ct
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {permissionKey}.", "FORBIDDEN");
    }

    // --- Mapping -----------------------------------------------------------------------------------------

    private static PlatformContentDefinitionDto ToDto(
        PlatformContentDefinition definition,
        IReadOnlyDictionary<Guid, int> versionNumberByVersionId
    ) =>
        new(
            definition.Id,
            definition.Kind,
            definition.Key,
            definition.DisplayName,
            definition.Description,
            definition.CurrentVersionId,
            definition.CurrentVersionId is { } id
            && versionNumberByVersionId.TryGetValue(id, out int v)
                ? v
                : null,
            definition.LatestDraftVersionId,
            definition.CreatedAt,
            definition.RetiredAt
        );

    private static PlatformContentVersionDto ToDto(PlatformContentVersion version) =>
        new(
            version.Id,
            version.DefinitionId,
            version.Version,
            version.ContentHash,
            version.PayloadJson,
            version.RenderGalleryRefs,
            version.PublishNote,
            version.DraftedAt,
            version.DraftedByPrincipalId,
            version.PublishedAt,
            version.PublishedByPrincipalId
        );

    private static PlatformContentPublishJobDto ToDto(PlatformContentPublishJob job) =>
        new(
            job.Id,
            job.DefinitionId,
            job.FromVersion,
            job.ToVersion,
            job.Mode,
            job.RequestedByPrincipalId,
            job.RequestedAt,
            job.PreviewAffectedCount,
            job.PreviewSkippedCount,
            job.ConfirmedAffectedCount,
            job.Status,
            job.CompletedAt,
            job.FailureReason,
            job.RebuildFailedWidgetIds,
            job.ValidationFailedPipelineIds
        );
}
