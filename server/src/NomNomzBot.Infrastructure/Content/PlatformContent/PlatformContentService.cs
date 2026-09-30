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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
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
/// backed by <see cref="Widget"/>), <c>Kind = "pipeline"</c> (system pipelines, backed by
/// <see cref="PipelineEntity"/>) and <c>Kind = "code_script"</c> (S-ADMIN-2e — sandboxed code scripts,
/// backed by <see cref="CodeScript"/>/<see cref="CodeScriptVersion"/>). A widget version's <c>PayloadJson</c>
/// is a <see cref="WidgetContentPayload"/> (Vue SFC source + default settings/subscriptions); publish compiles
/// the source through <see cref="IVueSfcCompiler"/> BEFORE anything is written — a widget that cannot
/// compile is rejected at publish time, never discovered by a viewer with a blank overlay. A pipeline
/// version's <c>PayloadJson</c> is the SAME wire-shape action graph <c>UpdatePipelineDto.GraphJsonCache</c>
/// accepts (the tree editor's own format, built by <c>PipelineGraphBuilder</c>) — publish never
/// re-implements graph validation or step persistence; it calls <see cref="IPipelineService.UpdateAsync"/> per
/// affected tenant, the SAME entry point the dashboard's own pipeline editor uses, so a system pipeline is
/// edited, validated and persisted through ONE machinery (platform-admin.md §2.2's "no second, worse
/// pipeline editor"). A code-script version's <c>PayloadJson</c> is a <see cref="CodeScriptContentPayload"/>
/// (raw source); publish compiles it through the SAME <see cref="IScriptExecutor.CompileAsync"/> validate-on-
/// save path a tenant's own editor save uses — a script that cannot compile is rejected at publish time,
/// never installed broken — and the fan-out writes the compiled bundle onto a NEW
/// <see cref="CodeScriptVersion"/> per affected tenant, hot-swapping <see cref="CodeScript.CurrentVersionId"/>
/// exactly like a tenant's own publish. Crucially, a platform-published script's compiled JS is later run by
/// the SAME <c>ScriptRunner</c>/hardened Jint sandbox (<c>JintEngineFactory.CreateHardened</c>) as any
/// tenant-authored script — there is no separate, wider-powered execution path for platform content
/// (S-ADMIN-2e's sandboxing-is-a-safety-property requirement). Every public method
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
    IPipelineService pipelineService,
    IScriptExecutor scriptExecutor,
    IEnumerable<IPlatformTemplateInstaller> templateInstallers
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
        int? currentVersion =
            definition.CurrentVersionId is { } currentId
            && versionNumberById.TryGetValue(currentId, out int number)
                ? number
                : null;

        return Result.Success(
            new PlatformContentDefinitionDetailDto(
                ToDto(definition, versionNumberById),
                [.. versions.Select(ToDto)],
                await SummarizeInstallsAsync(definition, currentVersion, ct)
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

        Result payloadOk = await ValidateTemplatePayloadAsync(
            request.Kind,
            request.PayloadJson,
            ct
        );
        if (payloadOk.IsFailure)
            return payloadOk.WithValue<PlatformContentDefinitionDto>(null!);

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

        Result payloadOk = await ValidateTemplatePayloadAsync(
            definition.Kind,
            request.PayloadJson,
            ct
        );
        if (payloadOk.IsFailure)
            return payloadOk.WithValue<PlatformContentVersionDto>(null!);

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

        PlatformContentDefinition definition = loaded.Value.Definition;

        PublishSelection selection = await SelectTenantRowsAsync(definition, mode, ct);

        // IgnoreQueryFilters + re-apply DeletedAt == null (the established cross-tenant-admin-read
        // convention, e.g. AdminService.cs): this preview spans EVERY tenant by design, but the
        // ambient tenant filter is set from the CALLING platform employee's OWN channel (ITenantScoped
        // has no way to know this read is meant to be platform-wide) — without this, a preview run by
        // an admin who also owns a channel silently sees only that one channel's row, undercounting
        // (or entirely missing) every other tenant's blast radius.
        List<string> sampleNames = definition.Kind switch
        {
            PlatformContentKinds.Widget => await db
                .Widgets.IgnoreQueryFilters()
                .Where(w => w.DeletedAt == null && selection.AffectedRowIds.Contains(w.Id))
                .Join(db.Channels, w => w.BroadcasterId, c => c.Id, (w, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
            PlatformContentKinds.Pipeline => await db
                .Pipelines.IgnoreQueryFilters()
                .Where(p => p.DeletedAt == null && selection.AffectedRowIds.Contains(p.Id))
                .Join(db.Channels, p => p.BroadcasterId, c => c.Id, (p, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
            PlatformContentKinds.CodeScript => await db
                .CodeScripts.IgnoreQueryFilters()
                .Where(s => s.DeletedAt == null && selection.AffectedRowIds.Contains(s.Id))
                .Join(db.Channels, s => s.BroadcasterId, c => c.Id, (s, c) => c.Name)
                .Take(10)
                .ToListAsync(ct),
            _ => await db
                .ChannelBuiltinCommands.IgnoreQueryFilters()
                .Where(b => b.DeletedAt == null && selection.AffectedRowIds.Contains(b.Id))
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

        if (definition.Kind == PlatformContentKinds.CodeScript)
        {
            Result compileGate = await ValidateCodeScriptPayloadCompilesAsync(
                version.PayloadJson,
                ct
            );
            if (compileGate.IsFailure)
                return compileGate.WithValue<PlatformContentPublishJobDto>(null!);
        }

        Result templateGate = await ValidateTemplatePayloadAsync(
            definition.Kind,
            version.PayloadJson,
            ct
        );
        if (templateGate.IsFailure)
            return templateGate.WithValue<PlatformContentPublishJobDto>(null!);

        PublishSelection freshSelection = await SelectTenantRowsAsync(definition, request.Mode, ct);
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
                else if (definition.Kind == PlatformContentKinds.CodeScript)
                {
                    CodeScriptFanOutResult codeScriptResult = await ApplyCodeScriptFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        now,
                        ct
                    );
                    confirmedCount = codeScriptResult.Count;
                    job.ValidationFailedCodeScriptIds =
                        codeScriptResult.ValidationFailedCodeScriptIds;
                }
                else if (definition.Kind == PlatformContentKinds.Command)
                {
                    confirmedCount = await ApplyCommandFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        now,
                        ct
                    );
                }
                else
                {
                    TemplateFanOutResult templateResult = await ApplyTemplateFanOutAsync(
                        definition,
                        version,
                        freshSelection.AffectedRowIds,
                        ct
                    );
                    confirmedCount = templateResult.Count;
                    job.UpdateFailedTemplateRowIds = templateResult.UpdateFailedRowIds;
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

        IPlatformTemplateInstaller? installer = FindTemplateInstaller(definition.Kind);
        return installer is null
            ? Result.Success()
            : await installer.ReleaseRetiredAsync(definition.Id, ct);
    }

    public async Task<Result<IReadOnlyList<EventResponsePresetDto>>> ListEventResponseTypesAsync(
        Guid actingPrincipalId,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            IamPermissionKeys.ContentRead,
            null,
            ct
        );
        return gate.WithValue(EventResponsePresetCatalog.Presets);
    }

    // --- Template kinds (installed per channel, validated here) ---------------------------------------

    /// <summary>For an installable template kind, checks the payload shape through the kind's own
    /// <see cref="IPlatformTemplateInstaller"/> — the same rules install applies. Other kinds pass.</summary>
    private Task<Result> ValidateTemplatePayloadAsync(
        string kind,
        string payloadJson,
        CancellationToken ct
    ) =>
        FindTemplateInstaller(kind)?.ValidatePayloadAsync(payloadJson, ct)
        ?? Task.FromResult(Result.Success());

    private IPlatformTemplateInstaller? FindTemplateInstaller(string kind) =>
        templateInstallers.FirstOrDefault(i => i.Kind == kind);

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

    /// <summary>Code-script kind's publish gate: compiles the payload's raw source through the SAME
    /// <see cref="IScriptExecutor.CompileAsync"/> validate-on-save path a tenant's own editor save uses — a
    /// stateless, DB-free check that runs BEFORE any tenant row is touched. Compilation is a pure function of
    /// the source text (no tenant state participates), so a payload that compiles here compiles identically
    /// for every affected tenant — unlike the pipeline kind's per-tenant graph validation, there is no
    /// tenant-dependent outcome to defer.</summary>
    private async Task<Result> ValidateCodeScriptPayloadCompilesAsync(
        string payloadJson,
        CancellationToken ct
    )
    {
        if (
            !CodeScriptContentPayload.TryParse(
                payloadJson,
                out CodeScriptContentPayload? payload,
                out string? error
            )
        )
            return Result.Failure(error!, "VALIDATION_FAILED");

        Result<ScriptCompilation> compiled = await scriptExecutor.CompileAsync(
            payload!.SourceCode,
            ct
        );
        return compiled.IsFailure
            ? Result.Failure(
                $"Code script source failed to compile: {compiled.ErrorMessage}",
                "VALIDATION_FAILED"
            )
            : Result.Success();
    }

    // --- Selection / fan-out -------------------------------------------------------------------------

    private readonly record struct PublishSelection(List<Guid> AffectedRowIds, int SkippedCount);

    /// <summary>
    /// Runs the EXACT SAME selection query publish uses (§2.1's "the preview runs the same query the
    /// publish will use" guarantee) against the REAL installed copies, computed BEFORE anything is written.
    /// Every kind lists its copies through <see cref="ListCopiesAsync"/>; the mode then decides: publish-as-new
    /// touches nothing, force takes every copy, and update-in-place takes only the copies whose live hash still
    /// matches the hash recorded at install or last sync — an edited copy is counted as skipped, never hidden.
    /// </summary>
    private async Task<PublishSelection> SelectTenantRowsAsync(
        PlatformContentDefinition definition,
        string mode,
        CancellationToken ct
    )
    {
        IReadOnlyList<PlatformContentCopy> copies = await ListCopiesAsync(definition, ct);

        switch (mode)
        {
            case PlatformContentPublishModes.PublishAsNew:
                // Zero blast radius by construction — nothing tenant-facing is touched.
                return new PublishSelection([], 0);

            case PlatformContentPublishModes.Force:
                return new PublishSelection([.. copies.Select(c => c.RowId)], 0);

            default:
                List<Guid> untouched = [];
                int skipped = 0;
                foreach (PlatformContentCopy copy in copies)
                {
                    if (copy.SourceHash == copy.LiveHash)
                        untouched.Add(copy.RowId);
                    else
                        skipped++;
                }
                return new PublishSelection(untouched, skipped);
        }
    }

    /// <summary>
    /// The installs summary the admin console shows beside a definition: counted from the same copies the
    /// publish selection reads, so "N behind, K edited" is exactly what an update-in-place publish would find.
    /// </summary>
    private async Task<PlatformContentInstallSummaryDto> SummarizeInstallsAsync(
        PlatformContentDefinition definition,
        int? currentVersion,
        CancellationToken ct
    )
    {
        IReadOnlyList<PlatformContentCopy> copies = await ListCopiesAsync(definition, ct);
        int edited = copies.Count(c => c.SourceHash != c.LiveHash);
        int behind = currentVersion is { } current
            ? copies.Count(c => c.SourceVersion is { } copied && copied < current)
            : 0;
        return new PlatformContentInstallSummaryDto(copies.Count, behind, edited);
    }

    /// <summary>
    /// Every tenant's installed copy of the definition, by kind. Cross-tenant by design (platform-admin.md
    /// §2.1's whole point): each listing bypasses the ambient tenant filter and re-applies
    /// <c>DeletedAt == null</c>, so the caller's own ambient tenant (if they happen to own a channel) never
    /// hides every OTHER tenant's row from the blast radius.
    /// </summary>
    private async Task<IReadOnlyList<PlatformContentCopy>> ListCopiesAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        switch (definition.Kind)
        {
            case PlatformContentKinds.Command:
                return await ListCommandCopiesAsync(definition, ct);
            case PlatformContentKinds.Widget:
                return await ListWidgetCopiesAsync(definition, ct);
            case PlatformContentKinds.Pipeline:
                return await ListPipelineCopiesAsync(definition, ct);
            case PlatformContentKinds.CodeScript:
                return await ListCodeScriptCopiesAsync(definition, ct);
            default:
                IPlatformTemplateInstaller? installer = FindTemplateInstaller(definition.Kind);
                return installer is null ? [] : await installer.ListCopiesAsync(definition.Id, ct);
        }
    }

    /// <summary>
    /// A builtin command has one row per channel, matched on <see cref="ChannelBuiltinCommand.BuiltinKey"/>.
    /// A row never stamped with provenance (pre-backfill / not from this definition) is listed with no source
    /// hash, so it reads as tenant-authored: skipped by update-in-place, counted as edited, taken only by force
    /// (§2.1's guardrail — never matched by name).
    /// </summary>
    private async Task<IReadOnlyList<PlatformContentCopy>> ListCommandCopiesAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        List<ChannelBuiltinCommand> installed = await db
            .ChannelBuiltinCommands.IgnoreQueryFilters()
            .Where(b => b.DeletedAt == null && b.BuiltinKey == definition.Key)
            .ToListAsync(ct);
        return
        [
            .. installed.Select(row =>
            {
                bool fromThisDefinition = row.PlatformSourceDefinitionId == definition.Id;
                return new PlatformContentCopy(
                    row.Id,
                    row.BroadcasterId,
                    fromThisDefinition ? row.PlatformSourceVersion : null,
                    fromThisDefinition ? row.PlatformSourceHash : null,
                    PlatformContentHash.ComputeHash(row.OverridesJson)
                );
            }),
        ];
    }

    /// <summary>
    /// The "installed" set for a widget definition is every tenant <see cref="Widget"/> row already stamped
    /// with THIS <see cref="PlatformContentDefinition.Id"/> — a widget is opt-in per tenant (created by
    /// install-from-gallery or an earlier publish), unlike a builtin command's one-row-per-channel shape, so
    /// there is no name-derived candidate set to fall back to (the seeder principle's "never match by name"
    /// guardrail applies here too). The stamped hash covers settings + subscriptions only, because the source
    /// lives on <see cref="WidgetVersion"/> rows, so a channel that edited the CODE is caught separately: a
    /// widget whose newest version came after its catalogue version (<see cref="Widget.IsSourceCustomized"/>)
    /// gets a live hash that can never equal the stamped one — it is counted as edited and an update-in-place
    /// publish skips it instead of overwriting the channel's code.
    /// </summary>
    private async Task<IReadOnlyList<PlatformContentCopy>> ListWidgetCopiesAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        List<Widget> installed = await db
            .Widgets.IgnoreQueryFilters()
            .Where(w => w.DeletedAt == null && w.PlatformSourceDefinitionId == definition.Id)
            .ToListAsync(ct);

        List<Guid> installedIds = [.. installed.Select(w => w.Id)];
        Dictionary<Guid, int> latestVersionNumbers = await db
            .WidgetVersions.IgnoreQueryFilters()
            .Where(v => installedIds.Contains(v.WidgetId))
            .GroupBy(v => v.WidgetId)
            .Select(g => new { WidgetId = g.Key, Latest = g.Max(v => v.VersionNumber) })
            .ToDictionaryAsync(x => x.WidgetId, x => x.Latest, ct);

        return
        [
            .. installed.Select(row =>
            {
                int? latest = latestVersionNumbers.TryGetValue(row.Id, out int number)
                    ? number
                    : null;
                string liveHash = row.IsSourceCustomized(latest)
                    ? PlatformContentHash.ComputeHash($$"""{"customizedSource":"{{row.Id}}"}""")
                    : WidgetContentPayload.ComputeSettingsHash(
                        row.Settings,
                        row.EventSubscriptions
                    );
                return new PlatformContentCopy(
                    row.Id,
                    row.BroadcasterId,
                    row.PlatformSourceVersion,
                    row.PlatformSourceHash,
                    liveHash
                );
            }),
        ];
    }

    /// <summary>
    /// The "installed" set for a pipeline definition is every tenant <see cref="PipelineEntity"/> row
    /// already stamped with THIS <see cref="PlatformContentDefinition.Id"/> — seeded by
    /// <c>RaidFlowSeeder</c>/<c>RaidStartFlowSeeder</c>/<c>RaidCommitFlowSeeder</c> (or a later install), same
    /// opt-in-per-tenant shape as the widget kind (no name-derived candidate set to fall back to; the
    /// seeder principle's "never match by name" guardrail applies here too).
    /// </summary>
    private async Task<IReadOnlyList<PlatformContentCopy>> ListPipelineCopiesAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        List<PipelineEntity> installed = await db
            .Pipelines.IgnoreQueryFilters()
            .Where(p => p.DeletedAt == null && p.PlatformSourceDefinitionId == definition.Id)
            .ToListAsync(ct);
        return
        [
            .. installed.Select(row => new PlatformContentCopy(
                row.Id,
                row.BroadcasterId,
                row.PlatformSourceVersion,
                row.PlatformSourceHash,
                PlatformContentHash.ComputeHash(row.GraphJsonCache)
            )),
        ];
    }

    /// <summary>
    /// The "installed" set for a code-script definition is every tenant <see cref="CodeScript"/> row already
    /// stamped with THIS <see cref="PlatformContentDefinition.Id"/> — opt-in-per-tenant, same shape as the
    /// widget/pipeline kinds (no name-derived candidate set to fall back to; the seeder principle's "never
    /// match by name" guardrail applies here too). The live hash is the hash of the row's OWN CURRENT
    /// VERSION's source — a tenant who authored a new version through their own editor (or never had one yet)
    /// reads as customized/no-baseline and is skipped, never guessed.
    /// </summary>
    private async Task<IReadOnlyList<PlatformContentCopy>> ListCodeScriptCopiesAsync(
        PlatformContentDefinition definition,
        CancellationToken ct
    )
    {
        List<CodeScript> installed = await db
            .CodeScripts.IgnoreQueryFilters()
            .Where(s => s.DeletedAt == null && s.PlatformSourceDefinitionId == definition.Id)
            .ToListAsync(ct);

        List<Guid> currentVersionIds =
        [
            .. installed
                .Where(s => s.CurrentVersionId != null)
                .Select(s => s.CurrentVersionId!.Value),
        ];
        Dictionary<Guid, string> sourceByVersionId = await db
            .CodeScriptVersions.IgnoreQueryFilters()
            .Where(v => v.DeletedAt == null && currentVersionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.SourceCode, ct);

        return
        [
            .. installed.Select(row =>
            {
                string? liveSource =
                    row.CurrentVersionId is { } vid
                    && sourceByVersionId.TryGetValue(vid, out string? src)
                        ? src
                        : null;
                string liveHash = liveSource is null
                    ? PlatformContentHash.ComputeHash(string.Empty)
                    : CodeScriptContentPayload.ComputeSourceHash(liveSource);
                return new PlatformContentCopy(
                    row.Id,
                    row.BroadcasterId,
                    row.PlatformSourceVersion,
                    row.PlatformSourceHash,
                    liveHash
                );
            }),
        ];
    }

    private readonly record struct TemplateFanOutResult(int Count, List<Guid> UpdateFailedRowIds);

    /// <summary>
    /// Rewrites every affected installed copy of a template kind through its installer's
    /// <see cref="IPlatformTemplateInstaller.UpdateCopyAsync"/> — the same feature save path the channel's own
    /// page uses (a reward change is pushed to that channel's Twitch, a timer keeps its bound pipeline) — and
    /// restamps provenance. A copy whose update is refused keeps its previous content and is recorded in
    /// <see cref="PlatformContentPublishJob.UpdateFailedTemplateRowIds"/>, never silently dropped. A customised
    /// copy was already excluded upstream by <see cref="SelectTenantRowsAsync"/> unless the mode is force.
    /// </summary>
    private async Task<TemplateFanOutResult> ApplyTemplateFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        CancellationToken ct
    )
    {
        IPlatformTemplateInstaller? installer = FindTemplateInstaller(definition.Kind);
        if (installer is null || affectedRowIds.Count == 0)
            return new TemplateFanOutResult(0, []);

        Dictionary<Guid, PlatformContentCopy> copiesById = (
            await installer.ListCopiesAsync(definition.Id, ct)
        ).ToDictionary(c => c.RowId);
        PlatformTemplateSource source = new(definition.Id, version.Version);

        int updated = 0;
        List<Guid> failed = [];
        foreach (Guid rowId in affectedRowIds)
        {
            if (!copiesById.TryGetValue(rowId, out PlatformContentCopy? copy))
                continue;
            Result outcome = await installer.UpdateCopyAsync(
                new PlatformTemplateCopyUpdate(
                    copy.RowId,
                    copy.BroadcasterId,
                    source,
                    version.PayloadJson
                ),
                ct
            );
            if (outcome.IsSuccess)
                updated++;
            else
                failed.Add(rowId);
        }
        return new TemplateFanOutResult(updated, failed);
    }

    private async Task<int> ApplyCommandFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        DateTime now,
        CancellationToken ct
    )
    {
        // Cross-tenant by design — without IgnoreQueryFilters this fan-out would silently write only the
        // acting platform employee's OWN channel's row (if any) and drop every other affected tenant, a
        // publish that reports success while doing far less than the preview promised.
        List<ChannelBuiltinCommand> targets = await db
            .ChannelBuiltinCommands.IgnoreQueryFilters()
            .Where(b => b.DeletedAt == null && affectedRowIds.Contains(b.Id))
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
    /// tenant was already excluded upstream by <see cref="SelectTenantRowsAsync"/> and never reaches this loop.
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

        // Cross-tenant by design — see the IgnoreQueryFilters note on ApplyCommandFanOutAsync above.
        List<Widget> targets = await db
            .Widgets.IgnoreQueryFilters()
            .Where(w => w.DeletedAt == null && affectedRowIds.Contains(w.Id))
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

            WidgetVersionDetail? rebuilt = await TryRebuildTenantBundleAsync(
                row,
                payload.SourceCode,
                ct
            );
            if (rebuilt is { BuildStatus: "success" })
            {
                // The new live version carries the catalogue's source verbatim — it is the widget's catalogue
                // baseline now, so a later channel save reads as an edit again. A failed rebuild keeps the old
                // baseline, the same rule the widget service applies to a failed reset.
                row.CatalogueVersionNumber = rebuilt.VersionNumber;
                await uow.SaveChangesAsync(ct);
            }
            else
            {
                rebuildFailedWidgetIds.Add(row.Id);
            }
        }
        return new WidgetFanOutResult(targets.Count, rebuildFailedWidgetIds);
    }

    /// <summary>
    /// One tenant's rebuild attempt: the version it appended (a build error is still a version, with an
    /// <c>error</c> status), or null when none was written. Never throws out of this method — an exception from the compiler/build pipeline is
    /// caught and treated as a rebuild failure (recorded, not swallowed) exactly like a normal build-error
    /// result, so one tenant's bad luck never aborts the rest of the fan-out.
    /// </summary>
    private async Task<WidgetVersionDetail?> TryRebuildTenantBundleAsync(
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
            return compiled.IsSuccess ? compiled.Value : null;
        }
        catch (Exception)
        {
            return null;
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

        // Cross-tenant by design — see the IgnoreQueryFilters note on ApplyCommandFanOutAsync above.
        List<PipelineEntity> targets = await db
            .Pipelines.IgnoreQueryFilters()
            .Where(p => p.DeletedAt == null && affectedRowIds.Contains(p.Id))
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

    private readonly record struct CodeScriptFanOutResult(
        int Count,
        List<Guid> ValidationFailedCodeScriptIds
    );

    /// <summary>
    /// Compiles the version's source ONCE (compilation is a pure function of the source text — see
    /// <see cref="ValidateCodeScriptPayloadCompilesAsync"/>) through the SAME
    /// <see cref="IScriptExecutor.CompileAsync"/> validate-on-save path a tenant's own editor save uses, then
    /// writes the compiled bundle onto a NEW <see cref="CodeScriptVersion"/> appended to every affected tenant
    /// <see cref="CodeScript"/>, hot-swapping <see cref="CodeScript.CurrentVersionId"/> to it — exactly the
    /// same append-only-version + hot-swap shape a tenant's own <c>CreateVersionAsync</c>/
    /// <c>PublishVersionAsync</c> produces. The compiled JS is later run by the SAME
    /// <c>ScriptRunner</c>/hardened Jint sandbox as any tenant-authored script (S-ADMIN-2e) — this fan-out
    /// never grants a platform-published script a wider capability or resource budget. A customized or
    /// deleted tenant was already excluded upstream by <see cref="SelectTenantRowsAsync"/> and never
    /// reaches this loop. Since compile success/failure cannot differ per tenant, a compile failure here (the
    /// gate already ran the identical check) fails every target uniformly rather than partially.
    /// </summary>
    private async Task<CodeScriptFanOutResult> ApplyCodeScriptFanOutAsync(
        PlatformContentDefinition definition,
        PlatformContentVersion version,
        List<Guid> affectedRowIds,
        DateTime now,
        CancellationToken ct
    )
    {
        if (
            !CodeScriptContentPayload.TryParse(
                version.PayloadJson,
                out CodeScriptContentPayload? payload,
                out _
            )
        )
            return new CodeScriptFanOutResult(0, [.. affectedRowIds]);

        Result<ScriptCompilation> compiled = await scriptExecutor.CompileAsync(
            payload!.SourceCode,
            ct
        );
        if (compiled.IsFailure)
            return new CodeScriptFanOutResult(0, [.. affectedRowIds]);

        // Cross-tenant by design — see the IgnoreQueryFilters note on ApplyCommandFanOutAsync above.
        List<CodeScript> targets = await db
            .CodeScripts.IgnoreQueryFilters()
            .Where(s => s.DeletedAt == null && affectedRowIds.Contains(s.Id))
            .ToListAsync(ct);

        Dictionary<Guid, int> maxVersionByScriptId = await db
            .CodeScriptVersions.IgnoreQueryFilters()
            .Where(v => v.DeletedAt == null && affectedRowIds.Contains(v.CodeScriptId))
            .GroupBy(v => v.CodeScriptId)
            .Select(g => new { g.Key, MaxVersion = g.Max(v => v.Version) })
            .ToDictionaryAsync(x => x.Key, x => x.MaxVersion, ct);

        string declaredCapabilitiesJson = JsonSerializer.Serialize(
            compiled.Value.DeclaredCapabilities
        );
        string sourceHash = payload.ComputeSourceHash();
        foreach (CodeScript row in targets)
        {
            int nextVersion = maxVersionByScriptId.TryGetValue(row.Id, out int maxVersion)
                ? maxVersion + 1
                : 1;
            CodeScriptVersion newVersion = new()
            {
                CodeScriptId = row.Id,
                BroadcasterId = row.BroadcasterId,
                Version = nextVersion,
                SourceCode = payload.SourceCode,
                CompiledJs = compiled.Value.CompiledJs,
                CompiledHash = compiled.Value.CompiledHash,
                ValidationStatus = "valid",
                DeclaredCapabilitiesJson = declaredCapabilitiesJson,
                PublishedAt = now,
            };
            db.CodeScriptVersions.Add(newVersion);

            row.CurrentVersionId = newVersion.Id;
            row.PlatformSourceDefinitionId = definition.Id;
            row.PlatformSourceVersion = version.Version;
            row.PlatformSourceHash = sourceHash;
            row.PlatformSourceSyncedAt = now;
        }

        return new CodeScriptFanOutResult(targets.Count, []);
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
                    or PlatformContentKinds.CodeScript
                )
            && FindTemplateInstaller(definition.Kind) is null
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
            job.ValidationFailedPipelineIds,
            job.ValidationFailedCodeScriptIds,
            job.UpdateFailedTemplateRowIds
        );
}
