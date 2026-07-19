// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Marketplace.Entities;
using NomNomzBot.Domain.Marketplace.Events;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// Validates and installs bundle ZIPs (marketplace.md §3). Entities are created through the owning module
/// services so each entity's own validation runs; any custom-code content lands DISABLED (D4); the install
/// is recorded as an <see cref="InstalledBundle"/> row so it can later be uninstalled exactly (D6). A
/// mid-import failure rolls back everything the import had already created — an install is all-or-nothing.
/// </summary>
public class BundleImportService : IBundleImportService
{
    /// <summary><c>InstalledBundle.Source</c> values (schema H.11 VC:enum).</summary>
    internal const string LocalSource = "local";
    internal const string MarketplaceSource = "marketplace";

    private readonly IApplicationDbContext _db;
    private readonly ICommandService _commands;
    private readonly IPipelineService _pipelines;
    private readonly IWidgetService _widgets;
    private readonly ISoundClipService _sounds;
    private readonly ICustomDataSourceService _dataSources;
    private readonly IEventResponseService _eventResponses;
    private readonly IRewardService _rewards;
    private readonly ITimerManagementService _timers;
    private readonly IChatTriggerService _chatTriggers;
    private readonly IPickListService _pickLists;
    private readonly ICodeScriptService _codeScripts;
    private readonly ICurrentTenantService _tenant;
    private readonly IEventBus _eventBus;

    public BundleImportService(
        IApplicationDbContext db,
        ICommandService commands,
        IPipelineService pipelines,
        IWidgetService widgets,
        ISoundClipService sounds,
        ICustomDataSourceService dataSources,
        IEventResponseService eventResponses,
        IRewardService rewards,
        ITimerManagementService timers,
        IChatTriggerService chatTriggers,
        IPickListService pickLists,
        ICodeScriptService codeScripts,
        ICurrentTenantService tenant,
        IEventBus eventBus
    )
    {
        _db = db;
        _commands = commands;
        _pipelines = pipelines;
        _widgets = widgets;
        _sounds = sounds;
        _dataSources = dataSources;
        _eventResponses = eventResponses;
        _rewards = rewards;
        _timers = timers;
        _chatTriggers = chatTriggers;
        _pickLists = pickLists;
        _codeScripts = codeScripts;
        _tenant = tenant;
        _eventBus = eventBus;
    }

    // ── Inspect ─────────────────────────────────────────────────────────────────

    public async Task<Result<BundleInspection>> InspectAsync(
        Guid broadcasterId,
        System.IO.Stream zip,
        CancellationToken ct = default
    )
    {
        Result<ParsedBundle> parsed = await ParseAsync(zip, ct);
        if (parsed.IsFailure)
            return parsed.ToTyped<BundleInspection>();

        return Result.Success(
            new BundleInspection(
                parsed.Value.Manifest,
                parsed.Value.Capabilities,
                parsed.Value.Issues
            )
        );
    }

    // ── Import ──────────────────────────────────────────────────────────────────

    public async Task<Result<InstalledBundleDto>> ImportAsync(
        Guid broadcasterId,
        Guid actorUserId,
        System.IO.Stream zip,
        ImportConflictPolicy policy,
        string? marketplaceItemId = null,
        CancellationToken ct = default
    )
    {
        Result<ParsedBundle> parsedResult = await ParseAsync(zip, ct);
        if (parsedResult.IsFailure)
            return parsedResult.ToTyped<InstalledBundleDto>();

        ParsedBundle bundle = parsedResult.Value;
        if (bundle.Issues.Count > 0)
            return Result.Failure<InstalledBundleDto>(
                $"The bundle failed inspection: {string.Join(" | ", bundle.Issues)}",
                "BUNDLE_INVALID"
            );

        // Marketplace re-install = UPDATE (D6): the previous version's entities give way first, then the
        // new content installs under the caller's conflict policy, and the SAME ledger row is rewritten —
        // the (BroadcasterId, Source, MarketplaceItemId) unique index never sees a second live row.
        InstalledBundle? existingInstall = null;
        if (marketplaceItemId is not null)
        {
            existingInstall = await _db.InstalledBundles.FirstOrDefaultAsync(
                b =>
                    b.BroadcasterId == broadcasterId
                    && b.Source == MarketplaceSource
                    && b.MarketplaceItemId == marketplaceItemId,
                ct
            );
            if (existingInstall is not null)
            {
                Dictionary<string, List<Guid>> previous =
                    JsonConvert.DeserializeObject<Dictionary<string, List<Guid>>>(
                        existingInstall.InstalledEntityIdsJson
                    ) ?? [];
                List<(string Type, Guid Id, string Name)> previousTargets = previous
                    .SelectMany(kv => kv.Value.Select(id => (kv.Key, id, string.Empty)))
                    .ToList();
                await DeleteCreatedAsync(broadcasterId, actorUserId, previousTargets, ct);
            }
        }

        string channelId = broadcasterId.ToString();
        List<(string Type, Guid Id, string Name)> created = [];
        Dictionary<string, Guid> pipelineIdsByName = [];
        Dictionary<string, Guid> scriptIdsByName = [];

        // Code scripts go through the tenant-ambient code-script module (custom-code.md broker invariant:
        // no BroadcasterId parameter). Refuse up front — BEFORE any write — when the resolved tenant is
        // not the import target, rather than creating scripts on the wrong channel.
        if (bundle.CodeScripts.Count > 0 && _tenant.BroadcasterId != broadcasterId)
            return Result.Failure<InstalledBundleDto>(
                "This bundle contains code scripts, which can only be imported while acting on the target channel.",
                "TENANT_MISMATCH"
            );

        try
        {
            // Code scripts first — they have no intra-bundle dependencies, and pipelines re-link their
            // run_code steps against them by bundle name (mirroring the command → pipeline name re-link).
            foreach (CodeScriptExport export in bundle.CodeScripts)
            {
                CodeScript? existing = await _db.CodeScripts.FirstOrDefaultAsync(
                    s =>
                        s.BroadcasterId == broadcasterId
                        && s.Name == export.Name
                        && s.DeletedAt == null,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                    {
                        // The skipped script still anchors run_code re-links — to the existing entity.
                        scriptIdsByName[export.Name] = existing.Id;
                        continue;
                    }
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _codeScripts.DeleteAsync(existing.Id, ct);
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: true,
                            maxLength: 100,
                            candidate =>
                                _db.CodeScripts.AnyAsync(
                                    s =>
                                        s.BroadcasterId == broadcasterId
                                        && s.Name == candidate
                                        && s.DeletedAt == null,
                                    ct
                                )
                        );
                    }
                }

                // Create + Version 1 from the entry file — validate-on-save recompiles the source on THIS
                // instance. A rejected compile still persists the script + version for audit, so the
                // failure path deletes that remnant before rolling the bundle back.
                string entrySource = export.Files[export.Manifest.Entry];
                Result<CodeScriptDetailDto> createdScript = await _codeScripts.CreateAsync(
                    new CreateCodeScriptRequest(name, export.Description, entrySource),
                    ct
                );
                if (createdScript.IsFailure)
                {
                    CodeScript? remnant = await _db.CodeScripts.FirstOrDefaultAsync(
                        s =>
                            s.BroadcasterId == broadcasterId
                            && s.Name == name
                            && s.DeletedAt == null,
                        ct
                    );
                    if (remnant is not null)
                        await _codeScripts.DeleteAsync(remnant.Id, CancellationToken.None);
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdScript.ErrorMessage,
                        createdScript.ErrorCode,
                        ct
                    );
                }
                created.Add((BundleFormat.CodeScriptType, createdScript.Value.Id, name));
                // Keyed by the EXPORT name (rename-on-collision notwithstanding) — the graphs re-link by it.
                scriptIdsByName[export.Name] = createdScript.Value.Id;

                // A multi-file project (or one declaring dependencies) is stored whole via the project
                // save, so the editor round-trips the full src/ tree on the importing instance too.
                if (export.Files.Count > 1 || export.Manifest.Dependencies.Count > 0)
                {
                    Result<CodeScriptVersionDto> savedProject = await _codeScripts.SaveProjectAsync(
                        createdScript.Value.Id,
                        new ProjectDto(
                            export.Files.ToDictionary(kv => kv.Key, kv => kv.Value),
                            new ProjectManifestDto(
                                export.Manifest.Entry,
                                export.Manifest.Kind,
                                export.Manifest.Framework,
                                export.Manifest.Dependencies
                            )
                        ),
                        ct
                    );
                    if (savedProject.IsFailure)
                        return await RollbackAsync(
                            broadcasterId,
                            actorUserId,
                            created,
                            savedProject.ErrorMessage,
                            savedProject.ErrorCode,
                            ct
                        );
                }

                // D4: imported custom code ALWAYS lands disabled — enabling is an explicit owner action.
                Result disabledScript = await _codeScripts.SetEnabledAsync(
                    createdScript.Value.Id,
                    false,
                    ct
                );
                if (disabledScript.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        disabledScript.ErrorMessage,
                        disabledScript.ErrorCode,
                        ct
                    );
            }

            // Pipelines next — commands re-link against them by bundle name.
            foreach (PipelineExport export in bundle.Pipelines)
            {
                Pipeline? existing = await _db.Pipelines.FirstOrDefaultAsync(
                    p => p.BroadcasterId == broadcasterId && p.Name == export.Name,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                    {
                        // The skipped pipeline still anchors command links — to the existing entity.
                        pipelineIdsByName[export.Name] = existing.Id;
                        continue;
                    }
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _pipelines.DeleteAsync(channelId, existing.Id, ct);
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: true,
                            maxLength: 200,
                            candidate =>
                                _db.Pipelines.AnyAsync(
                                    p => p.BroadcasterId == broadcasterId && p.Name == candidate,
                                    ct
                                )
                        );
                    }
                }

                // run_code re-link: every step carrying a code_script_name gets its code_script_id set to
                // the script imported from this bundle (or an existing tenant script with that name). An
                // unresolvable name fails typed BEFORE this pipeline is created.
                Result<string?> relinked = await RelinkRunCodeAsync(
                    broadcasterId,
                    export.GraphJson,
                    scriptIdsByName,
                    ct
                );
                if (relinked.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        relinked.ErrorMessage,
                        relinked.ErrorCode,
                        ct
                    );
                string? graphJson = relinked.Value;

                // D4: a run_code-bearing pipeline always lands disabled, whatever the export says.
                bool hasRunCode = BundleConventions.ContainsRunCode(graphJson);
                CreatePipelineDto request = new()
                {
                    Name = name,
                    Description = export.Description,
                    TriggerKind = export.TriggerKind,
                    IsEnabled = export.IsEnabled && !hasRunCode,
                    GraphJsonCache = graphJson is not null
                        ? System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            graphJson
                        )
                        : null,
                };
                Result<PipelineDto> createdPipeline = await _pipelines.CreateAsync(
                    channelId,
                    request,
                    ct
                );
                if (createdPipeline.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdPipeline.ErrorMessage,
                        createdPipeline.ErrorCode,
                        ct
                    );

                pipelineIdsByName[export.Name] = createdPipeline.Value.Id;
                created.Add((BundleFormat.PipelineType, createdPipeline.Value.Id, name));
            }

            foreach (CommandExport export in bundle.Commands)
            {
                string normalized = export.Name.ToLowerInvariant();
                Command? existing = await _db.Commands.FirstOrDefaultAsync(
                    c => c.BroadcasterId == broadcasterId && c.NameNormalized == normalized,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _commands.DeleteAsync(channelId, existing.Name, ct);
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: false,
                            maxLength: 100,
                            candidate =>
                            {
                                string candidateNormalized = candidate.ToLowerInvariant();
                                return _db.Commands.AnyAsync(
                                    c =>
                                        c.BroadcasterId == broadcasterId
                                        && c.NameNormalized == candidateNormalized,
                                    ct
                                );
                            }
                        );
                    }
                }

                CreateCommandDto request = new()
                {
                    Name = name,
                    Tier = export.Tier,
                    MinPermissionLevel = export.MinPermissionLevel,
                    TemplateResponse = export.TemplateResponse,
                    TemplateResponses = export.TemplateResponses?.ToList(),
                    PipelineId = export.PipelineName is not null
                        ? pipelineIdsByName.GetValueOrDefault(export.PipelineName)
                        : null,
                    CooldownSeconds = export.CooldownSeconds,
                    CooldownPerUser = export.CooldownPerUser,
                    Description = export.Description,
                    Aliases = export.Aliases.ToList(),
                };
                Result<CommandDto> createdCommand = await _commands.CreateAsync(
                    channelId,
                    request,
                    ct
                );
                if (createdCommand.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdCommand.ErrorMessage,
                        createdCommand.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.CommandType, createdCommand.Value.Id, name));

                // D4: imported custom-code commands land disabled; a disabled export also stays disabled.
                if (export.Tier == "code" || !export.IsEnabled)
                {
                    Result<CommandDto> disabled = await _commands.UpdateAsync(
                        channelId,
                        name,
                        new UpdateCommandDto { IsEnabled = false },
                        ct
                    );
                    if (disabled.IsFailure)
                        return await RollbackAsync(
                            broadcasterId,
                            actorUserId,
                            created,
                            disabled.ErrorMessage,
                            disabled.ErrorCode,
                            ct
                        );
                }
            }

            // Event responses are keyed per event type — every channel already (lazily) has one row per
            // catalog event, so the import UPSERTS by EventType regardless of the conflict policy. A
            // rollback/uninstall deletes the row; the module's lazy seed restores the disabled default.
            foreach (EventResponseExport export in bundle.EventResponses)
            {
                Result<EventResponseDto> upserted = await _eventResponses.UpsertAsync(
                    channelId,
                    export.EventType,
                    new UpdateEventResponseDto
                    {
                        IsEnabled = export.IsEnabled,
                        ResponseType = export.ResponseType,
                        Message = export.Message,
                        PipelineId = export.PipelineName is not null
                            ? pipelineIdsByName.GetValueOrDefault(export.PipelineName)
                            : null,
                        Metadata = export.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                    },
                    ct
                );
                if (upserted.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        upserted.ErrorMessage,
                        upserted.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.EventResponseType, upserted.Value.Id, export.EventType));
            }

            foreach (RewardExport export in bundle.Rewards)
            {
                Reward? existing = await _db.Rewards.FirstOrDefaultAsync(
                    r => r.BroadcasterId == broadcasterId && r.Title == export.Title,
                    ct
                );
                string title = export.Title;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _rewards.DeleteAsync(
                            channelId,
                            existing.Id.ToString(),
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        title = await FreeNameAsync(
                            export.Title,
                            freeText: true,
                            maxLength: 255,
                            candidate =>
                                _db.Rewards.AnyAsync(
                                    r => r.BroadcasterId == broadcasterId && r.Title == candidate,
                                    ct
                                )
                        );
                    }
                }

                // D2: the import creates a LOCAL, bot-manageable definition (no TwitchRewardId) — the
                // channel's existing sync/recreate endpoints push it to Twitch later, so Helix being
                // unavailable can never fail the bundle.
                Result<RewardDetail> createdReward = await _rewards.CreateAsync(
                    channelId,
                    new CreateRewardRequest
                    {
                        Title = title,
                        Cost = export.Cost,
                        Prompt = export.Description,
                        Response = export.Response,
                        TimerDurationSeconds = export.TimerDurationSeconds,
                        PipelineId = export.PipelineName is not null
                            ? pipelineIdsByName.GetValueOrDefault(export.PipelineName)
                            : null,
                    },
                    ct
                );
                if (createdReward.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdReward.ErrorMessage,
                        createdReward.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.RewardType, Guid.Parse(createdReward.Value.Id), title));

                // A local reward has no Twitch id, so this disable applies locally without a Helix call.
                if (!export.IsEnabled)
                {
                    Result<RewardDetail> disabled = await _rewards.UpdateAsync(
                        channelId,
                        createdReward.Value.Id,
                        new UpdateRewardRequest { IsEnabled = false },
                        ct
                    );
                    if (disabled.IsFailure)
                        return await RollbackAsync(
                            broadcasterId,
                            actorUserId,
                            created,
                            disabled.ErrorMessage,
                            disabled.ErrorCode,
                            ct
                        );
                }
            }

            foreach (TimerExport export in bundle.Timers)
            {
                DomainTimer? existing = await _db.Timers.FirstOrDefaultAsync(
                    t => t.BroadcasterId == broadcasterId && t.Name == export.Name,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _timers.DeleteAsync(channelId, existing.Id, ct);
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: true,
                            maxLength: 100,
                            candidate =>
                                _db.Timers.AnyAsync(
                                    t => t.BroadcasterId == broadcasterId && t.Name == candidate,
                                    ct
                                )
                        );
                    }
                }

                Result<TimerDto> createdTimer = await _timers.CreateAsync(
                    channelId,
                    new CreateTimerDto
                    {
                        Name = name,
                        Messages = export.Messages.ToList(),
                        PipelineId = export.PipelineName is not null
                            ? pipelineIdsByName.GetValueOrDefault(export.PipelineName)
                            : null,
                        IntervalMinutes = export.IntervalMinutes,
                        MinChatActivity = export.MinChatActivity,
                        IsEnabled = export.IsEnabled,
                        FireOnce = export.FireOnce,
                    },
                    ct
                );
                if (createdTimer.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdTimer.ErrorMessage,
                        createdTimer.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.TimerType, createdTimer.Value.Id, name));
            }

            foreach (ChatTriggerExport export in bundle.ChatTriggers)
            {
                ChatTrigger? existing = await _db.ChatTriggers.FirstOrDefaultAsync(
                    t => t.BroadcasterId == broadcasterId && t.Pattern == export.Pattern,
                    ct
                );
                string pattern = export.Pattern;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _chatTriggers.DeleteAsync(
                            channelId,
                            existing.Id,
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        pattern = await FreeNameAsync(
                            export.Pattern,
                            freeText: true,
                            maxLength: 200,
                            candidate =>
                                _db.ChatTriggers.AnyAsync(
                                    t => t.BroadcasterId == broadcasterId && t.Pattern == candidate,
                                    ct
                                )
                        );
                    }
                }

                Result<ChatTriggerDto> createdTrigger = await _chatTriggers.CreateAsync(
                    channelId,
                    new CreateChatTriggerRequest
                    {
                        Pattern = pattern,
                        MatchType = export.MatchType,
                        CaseSensitive = export.CaseSensitive,
                        IsEnabled = export.IsEnabled,
                        Response = export.Response,
                        PipelineId = export.PipelineName is not null
                            ? pipelineIdsByName.GetValueOrDefault(export.PipelineName)
                            : null,
                        CooldownSeconds = export.CooldownSeconds,
                        MinPermissionLevel = export.MinPermissionLevel,
                    },
                    ct
                );
                if (createdTrigger.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdTrigger.ErrorMessage,
                        createdTrigger.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.ChatTriggerType, createdTrigger.Value.Id, pattern));
            }

            foreach (PickListExport export in bundle.PickLists)
            {
                PickList? existing = await _db
                    .PickLists.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        p =>
                            p.BroadcasterId == broadcasterId
                            && p.Name == export.Name
                            && p.DeletedAt == null,
                        ct
                    );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _pickLists.DeleteAsync(
                            broadcasterId,
                            existing.Id,
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: false,
                            maxLength: 100,
                            candidate =>
                                _db.PickLists.IgnoreQueryFilters()
                                    .AnyAsync(
                                        p =>
                                            p.BroadcasterId == broadcasterId
                                            && p.Name == candidate
                                            && p.DeletedAt == null,
                                        ct
                                    )
                        );
                    }
                }

                Result<PickListDto> createdList = await _pickLists.CreateAsync(
                    broadcasterId,
                    new CreatePickListRequest(name, export.Description, export.Items.ToList()),
                    ct
                );
                if (createdList.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdList.ErrorMessage,
                        createdList.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.PickListType, createdList.Value.Id, name));
            }

            foreach (WidgetExport export in bundle.Widgets)
            {
                Widget? existing = await _db.Widgets.FirstOrDefaultAsync(
                    w => w.BroadcasterId == broadcasterId && w.Name == export.Name,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _widgets.DeleteAsync(
                            channelId,
                            existing.Id.ToString(),
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: true,
                            maxLength: 255,
                            candidate =>
                                _db.Widgets.AnyAsync(
                                    w => w.BroadcasterId == broadcasterId && w.Name == candidate,
                                    ct
                                )
                        );
                    }
                }

                CreateWidgetRequest request = new()
                {
                    Name = name,
                    Framework = export.Framework,
                    Description = export.Description,
                    Settings = export.Settings.ToDictionary(kv => kv.Key, kv => kv.Value),
                    EventSubscriptions = export.EventSubscriptions.ToList(),
                };
                Result<WidgetDetail> createdWidget = await _widgets.CreateAsync(
                    channelId,
                    request,
                    ct
                );
                if (createdWidget.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdWidget.ErrorMessage,
                        createdWidget.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.WidgetType, createdWidget.Value.Id, name));

                // Compile-on-import so the widget is servable. A failed build persists as an inspectable
                // error version on the widget — the install itself still stands.
                if (export.SourceCode is not null)
                {
                    await _widgets.CompileAsync(
                        channelId,
                        createdWidget.Value.Id.ToString(),
                        new CompileWidgetRequest { SourceCode = export.SourceCode },
                        ct
                    );
                }
            }

            foreach ((SoundExport export, byte[] audio) in bundle.Sounds)
            {
                SoundClip? existing = await _db.SoundClips.FirstOrDefaultAsync(
                    s => s.BroadcasterId == broadcasterId && s.Name == export.Name,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _sounds.DeleteAsync(
                            broadcasterId,
                            existing.Id,
                            actorUserId,
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: false,
                            maxLength: 50,
                            candidate =>
                                _db.SoundClips.AnyAsync(
                                    s => s.BroadcasterId == broadcasterId && s.Name == candidate,
                                    ct
                                )
                        );
                    }
                }

                // Re-upload through the sound module: format/size validation + duration probing run again.
                using MemoryStream content = new(audio);
                Result<SoundClipDto> createdSound = await _sounds.UploadAsync(
                    broadcasterId,
                    actorUserId,
                    new UploadSoundClipRequest(
                        name,
                        export.DisplayName,
                        Path.GetFileName(export.AudioPath),
                        export.MimeType,
                        content,
                        export.DefaultVolume
                    ),
                    ct
                );
                if (createdSound.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdSound.ErrorMessage,
                        createdSound.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.SoundType, createdSound.Value.Id, name));
            }

            foreach (CustomDataSourceExport export in bundle.DataSources)
            {
                CustomDataSource? existing = await _db.CustomDataSources.FirstOrDefaultAsync(
                    d => d.BroadcasterId == broadcasterId && d.Name == export.Name,
                    ct
                );
                string name = export.Name;
                if (existing is not null)
                {
                    if (policy == ImportConflictPolicy.Skip)
                        continue;
                    if (policy == ImportConflictPolicy.Overwrite)
                    {
                        Result deleted = await _dataSources.DeleteAsync(
                            broadcasterId,
                            existing.Id,
                            actorUserId,
                            ct
                        );
                        if (deleted.IsFailure)
                            return await RollbackAsync(
                                broadcasterId,
                                actorUserId,
                                created,
                                deleted.ErrorMessage,
                                deleted.ErrorCode,
                                ct
                            );
                    }
                    else
                    {
                        name = await FreeNameAsync(
                            export.Name,
                            freeText: false,
                            maxLength: 50,
                            candidate =>
                                _db.CustomDataSources.AnyAsync(
                                    d => d.BroadcasterId == broadcasterId && d.Name == candidate,
                                    ct
                                )
                        );
                    }
                }

                // D2/D4: no credential travels — the import lands disabled with an empty secret.
                Result<CustomDataSourceDto> createdSource = await _dataSources.CreateAsync(
                    broadcasterId,
                    actorUserId,
                    new UpsertCustomDataSourceRequest(
                        name,
                        export.DisplayName,
                        export.SourceKind,
                        export.PresetKey,
                        export.EndpointUrl,
                        AuthSecret: null,
                        export.FieldMap,
                        export.PollIntervalSeconds,
                        IsEnabled: false
                    ),
                    ct
                );
                if (createdSource.IsFailure)
                    return await RollbackAsync(
                        broadcasterId,
                        actorUserId,
                        created,
                        createdSource.ErrorMessage,
                        createdSource.ErrorCode,
                        ct
                    );
                created.Add((BundleFormat.CustomDataSourceType, createdSource.Value.Id, name));
            }
        }
        catch (Exception)
        {
            await DeleteCreatedAsync(broadcasterId, actorUserId, created, CancellationToken.None);
            throw;
        }

        // ── Record the install + announce it ──
        Dictionary<string, List<Guid>> installedIds = created
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        InstalledBundle row =
            existingInstall
            ?? new InstalledBundle
            {
                BroadcasterId = broadcasterId,
                Source = marketplaceItemId is null ? LocalSource : MarketplaceSource,
                MarketplaceItemId = marketplaceItemId,
            };
        row.Name = bundle.Manifest.Metadata.Name;
        row.Version = bundle.Manifest.Metadata.Version;
        row.Author = bundle.Manifest.Metadata.Author;
        row.License = bundle.Manifest.Metadata.License;
        row.ManifestJson = BundleConventions.Serialize(bundle.Manifest);
        row.InstalledEntityIdsJson = BundleConventions.Serialize(installedIds);
        row.InstalledByUserId = actorUserId;
        if (existingInstall is null)
            _db.InstalledBundles.Add(row);
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new BundleInstalledEvent
            {
                BroadcasterId = broadcasterId,
                InstalledBundleId = row.Id,
                Name = row.Name,
                Source = row.Source,
                Capabilities = bundle.Capabilities,
            },
            ct
        );

        return Result.Success(ToDto(row));
    }

    // ── run_code re-link (import side) ──────────────────────────────────────────

    /// <summary>
    /// Re-links every <c>run_code</c> step in a bundled pipeline graph: a step carrying
    /// <c>code_script_name</c> gets <c>code_script_id</c> set to the script imported from the same bundle
    /// (falling back to an existing tenant script with that name); the name stays as a harmless hint. An
    /// unresolvable name fails <c>BUNDLE_INVALID</c> so the pipeline is never created against a dangling
    /// reference. Steps without a name are left untouched — they run fail-closed at execution time.
    /// </summary>
    private async Task<Result<string?>> RelinkRunCodeAsync(
        Guid broadcasterId,
        string? graphJson,
        Dictionary<string, Guid> scriptIdsByName,
        CancellationToken ct
    )
    {
        JsonObject? graph = BundleConventions.ParseGraph(graphJson);
        if (graph is null)
            return Result.Success(graphJson);

        bool rewritten = false;
        foreach (JsonObject bag in BundleConventions.RunCodeParameterBags(graph))
        {
            string? scriptName = BundleConventions.GetStringParam(
                bag,
                BundleConventions.CodeScriptNameParam
            );
            if (string.IsNullOrWhiteSpace(scriptName))
                continue;

            Guid scriptId;
            if (scriptIdsByName.TryGetValue(scriptName, out Guid bundledId))
            {
                scriptId = bundledId;
            }
            else
            {
                CodeScript? existing = await _db.CodeScripts.FirstOrDefaultAsync(
                    s =>
                        s.BroadcasterId == broadcasterId
                        && s.Name == scriptName
                        && s.DeletedAt == null,
                    ct
                );
                if (existing is null)
                    return Result.Failure<string?>(
                        $"A pipeline runs code script '{scriptName}', which is neither in the bundle nor on this channel.",
                        "BUNDLE_INVALID"
                    );
                scriptId = existing.Id;
            }

            bag[BundleConventions.CodeScriptIdParam] = scriptId.ToString();
            rewritten = true;
        }

        return Result.Success(rewritten ? graph.ToJsonString() : graphJson);
    }

    // ── Ledger ──────────────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<InstalledBundleDto>>> ListInstalledAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<InstalledBundle> rows = await _db
            .InstalledBundles.Where(b => b.BroadcasterId == broadcasterId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<InstalledBundleDto>>(rows.Select(ToDto).ToList());
    }

    public async Task<Result> UninstallAsync(
        Guid broadcasterId,
        Guid installedBundleId,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        InstalledBundle? row = await _db.InstalledBundles.FirstOrDefaultAsync(
            b => b.BroadcasterId == broadcasterId && b.Id == installedBundleId,
            ct
        );
        if (row is null)
            return Result.Failure(
                $"Installed bundle '{installedBundleId}' was not found.",
                "NOT_FOUND"
            );

        Dictionary<string, List<Guid>> installedIds =
            JsonConvert.DeserializeObject<Dictionary<string, List<Guid>>>(
                row.InstalledEntityIdsJson
            ) ?? [];

        List<(string Type, Guid Id, string Name)> targets = installedIds
            .SelectMany(kv => kv.Value.Select(id => (kv.Key, id, string.Empty)))
            .ToList();
        await DeleteCreatedAsync(broadcasterId, actorUserId, targets, ct);

        _db.InstalledBundles.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ── Parsing ─────────────────────────────────────────────────────────────────

    private sealed record ParsedBundle(
        BundleManifest Manifest,
        List<PipelineExport> Pipelines,
        List<CommandExport> Commands,
        List<WidgetExport> Widgets,
        List<(SoundExport Export, byte[] Audio)> Sounds,
        List<CustomDataSourceExport> DataSources,
        List<EventResponseExport> EventResponses,
        List<RewardExport> Rewards,
        List<TimerExport> Timers,
        List<ChatTriggerExport> ChatTriggers,
        List<PickListExport> PickLists,
        List<CodeScriptExport> CodeScripts,
        IReadOnlyList<string> Capabilities,
        List<string> Issues
    );

    private static async Task<Result<ParsedBundle>> ParseAsync(
        System.IO.Stream zip,
        CancellationToken ct
    )
    {
        if (zip.CanSeek && zip.Length > BundleFormat.MaxBundleBytes)
            return TooLarge();

        // Buffer with a hard cap so a non-seekable upload can never balloon memory past the limit.
        MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await zip.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > BundleFormat.MaxBundleBytes)
                return TooLarge();
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException)
        {
            return Result.Failure<ParsedBundle>(
                "The uploaded file is not a valid ZIP archive.",
                "BUNDLE_INVALID"
            );
        }

        using (archive)
        {
            ZipArchiveEntry? manifestEntry = archive.GetEntry(BundleFormat.ManifestEntryName);
            if (manifestEntry is null)
                return Result.Failure<ParsedBundle>(
                    "The bundle has no manifest.json.",
                    "BUNDLE_INVALID"
                );

            BundleManifest? manifest;
            try
            {
                manifest = BundleConventions.Deserialize<BundleManifest>(
                    await ReadEntryTextAsync(manifestEntry, ct)
                );
            }
            catch (JsonException)
            {
                manifest = null;
            }
            if (manifest?.Metadata is null)
                return Result.Failure<ParsedBundle>(
                    "The bundle's manifest.json is malformed.",
                    "BUNDLE_INVALID"
                );

            List<string> issues = [];
            if (manifest.SchemaVersion != BundleFormat.CurrentSchemaVersion)
                issues.Add(
                    $"Unknown manifest schemaVersion {manifest.SchemaVersion} — this instance understands version {BundleFormat.CurrentSchemaVersion}."
                );
            if (string.IsNullOrWhiteSpace(manifest.Metadata.Name))
                issues.Add("The bundle metadata has no name.");
            if (string.IsNullOrWhiteSpace(manifest.Metadata.Version))
                issues.Add("The bundle metadata has no version.");
            if (manifest.Items.Count == 0)
                issues.Add("The bundle contains no items.");

            List<PipelineExport> pipelines = [];
            List<CommandExport> commands = [];
            List<WidgetExport> widgets = [];
            List<(SoundExport, byte[])> sounds = [];
            List<CustomDataSourceExport> dataSources = [];
            List<EventResponseExport> eventResponses = [];
            List<RewardExport> rewards = [];
            List<TimerExport> timers = [];
            List<ChatTriggerExport> chatTriggers = [];
            List<PickListExport> pickLists = [];
            List<CodeScriptExport> codeScripts = [];

            foreach (BundleManifestItem item in manifest.Items)
            {
                switch (item.Type)
                {
                    case BundleFormat.PipelineType:
                    {
                        PipelineExport? export = await ReadItemAsync<PipelineExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            pipelines.Add(export);
                        break;
                    }
                    case BundleFormat.CommandType:
                    {
                        CommandExport? export = await ReadItemAsync<CommandExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            commands.Add(export);
                        break;
                    }
                    case BundleFormat.WidgetType:
                    {
                        WidgetExport? export = await ReadItemAsync<WidgetExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            widgets.Add(export);
                        break;
                    }
                    case BundleFormat.SoundType:
                    {
                        SoundExport? export = await ReadItemAsync<SoundExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is null
                            || !Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            break;
                        ZipArchiveEntry? audioEntry = string.IsNullOrWhiteSpace(export.AudioPath)
                            ? null
                            : archive.GetEntry(export.AudioPath);
                        if (audioEntry is null)
                        {
                            issues.Add(
                                $"Sound '{item.Name}' has no audio payload at '{export.AudioPath}'."
                            );
                            break;
                        }
                        await using MemoryStream audio = new();
                        await using (System.IO.Stream audioStream = audioEntry.Open())
                        {
                            await audioStream.CopyToAsync(audio, ct);
                        }
                        sounds.Add((export, audio.ToArray()));
                        break;
                    }
                    case BundleFormat.CustomDataSourceType:
                    {
                        CustomDataSourceExport? export =
                            await ReadItemAsync<CustomDataSourceExport>(archive, item, issues, ct);
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            dataSources.Add(export);
                        break;
                    }
                    case BundleFormat.EventResponseType:
                    {
                        EventResponseExport? export = await ReadItemAsync<EventResponseExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.EventType, issues)
                        )
                            eventResponses.Add(export);
                        break;
                    }
                    case BundleFormat.RewardType:
                    {
                        RewardExport? export = await ReadItemAsync<RewardExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Title, issues)
                        )
                            rewards.Add(export);
                        break;
                    }
                    case BundleFormat.TimerType:
                    {
                        TimerExport? export = await ReadItemAsync<TimerExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            timers.Add(export);
                        break;
                    }
                    case BundleFormat.ChatTriggerType:
                    {
                        ChatTriggerExport? export = await ReadItemAsync<ChatTriggerExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Pattern, issues)
                        )
                            chatTriggers.Add(export);
                        break;
                    }
                    case BundleFormat.PickListType:
                    {
                        PickListExport? export = await ReadItemAsync<PickListExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is not null
                            && Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            pickLists.Add(export);
                        break;
                    }
                    case BundleFormat.CodeScriptType:
                    {
                        CodeScriptExport? export = await ReadItemAsync<CodeScriptExport>(
                            archive,
                            item,
                            issues,
                            ct
                        );
                        if (
                            export is null
                            || !Validate(item, export.SchemaVersion, export.Name, issues)
                        )
                            break;
                        if (
                            export.Manifest is null
                            || !export.Files.ContainsKey(export.Manifest.Entry)
                        )
                        {
                            issues.Add(
                                $"Code script '{item.Name}' has no project entry file — the manifest entry must be present in its file set."
                            );
                            break;
                        }
                        codeScripts.Add(export);
                        break;
                    }
                    default:
                        issues.Add($"Item '{item.Name}' has an unknown type '{item.Type}'.");
                        break;
                }
            }

            // Every pipeline link must resolve inside the bundle — an import never links to an id. This
            // fails the bundle at inspect, BEFORE any write.
            HashSet<string> bundledPipelineNames = pipelines
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
            void RequireBundledPipeline(string itemKind, string itemName, string? pipelineName)
            {
                if (pipelineName is not null && !bundledPipelineNames.Contains(pipelineName))
                    issues.Add(
                        $"{itemKind} '{itemName}' depends on pipeline '{pipelineName}', which is not in the bundle."
                    );
            }
            foreach (CommandExport command in commands)
                RequireBundledPipeline("Command", command.Name, command.PipelineName);
            foreach (EventResponseExport response in eventResponses)
                RequireBundledPipeline("Event response", response.EventType, response.PipelineName);
            foreach (RewardExport reward in rewards)
                RequireBundledPipeline("Reward", reward.Title, reward.PipelineName);
            foreach (TimerExport timer in timers)
                RequireBundledPipeline("Timer", timer.Name, timer.PipelineName);
            foreach (ChatTriggerExport trigger in chatTriggers)
                RequireBundledPipeline("Chat trigger", trigger.Pattern, trigger.PipelineName);

            // Every `code_script:<name>` edge must resolve inside the bundle too (run_code re-link) —
            // like the pipeline edges, this fails the bundle at inspect, BEFORE any write.
            HashSet<string> bundledScriptNames = codeScripts
                .Select(s => s.Name)
                .ToHashSet(StringComparer.Ordinal);
            string scriptEdgePrefix = $"{BundleFormat.CodeScriptType}:";
            foreach (BundleManifestItem item in manifest.Items)
            {
                foreach (string dependency in item.Dependencies ?? [])
                {
                    if (!dependency.StartsWith(scriptEdgePrefix, StringComparison.Ordinal))
                        continue;
                    string scriptName = dependency[scriptEdgePrefix.Length..];
                    if (!bundledScriptNames.Contains(scriptName))
                        issues.Add(
                            $"{item.Type} '{item.Name}' depends on code script '{scriptName}', which is not in the bundle."
                        );
                }
            }

            IReadOnlyList<string> capabilities = BundleConventions.CollectCapabilities(
                pipelines,
                commands,
                codeScripts,
                hasWidgets: widgets.Count > 0,
                hasSounds: sounds.Count > 0,
                hasDataSources: dataSources.Count > 0,
                hasEventResponses: eventResponses.Count > 0,
                hasRewards: rewards.Count > 0,
                hasTimers: timers.Count > 0,
                hasChatTriggers: chatTriggers.Count > 0,
                hasPickLists: pickLists.Count > 0
            );

            return Result.Success(
                new ParsedBundle(
                    manifest,
                    pipelines,
                    commands,
                    widgets,
                    sounds,
                    dataSources,
                    eventResponses,
                    rewards,
                    timers,
                    chatTriggers,
                    pickLists,
                    codeScripts,
                    capabilities,
                    issues
                )
            );
        }

        static Result<ParsedBundle> TooLarge() =>
            Result.Failure<ParsedBundle>(
                $"The bundle is larger than {BundleFormat.MaxBundleBytes / (1024 * 1024)} MB.",
                "BUNDLE_TOO_LARGE"
            );
    }

    private static async Task<TExport?> ReadItemAsync<TExport>(
        ZipArchive archive,
        BundleManifestItem item,
        List<string> issues,
        CancellationToken ct
    )
        where TExport : class
    {
        ZipArchiveEntry? entry = string.IsNullOrWhiteSpace(item.Path)
            ? null
            : archive.GetEntry(item.Path);
        if (entry is null)
        {
            issues.Add($"Item '{item.Name}' has no entry at '{item.Path}'.");
            return null;
        }

        try
        {
            TExport? export = BundleConventions.Deserialize<TExport>(
                await ReadEntryTextAsync(entry, ct)
            );
            if (export is null)
                issues.Add($"Item '{item.Name}' is malformed.");
            return export;
        }
        catch (JsonException)
        {
            issues.Add($"Item '{item.Name}' is malformed.");
            return null;
        }
    }

    private static bool Validate(
        BundleManifestItem item,
        int schemaVersion,
        string? name,
        List<string> issues
    )
    {
        if (schemaVersion != BundleFormat.CurrentSchemaVersion)
        {
            issues.Add(
                $"Item '{item.Name}' has an unknown schemaVersion {schemaVersion} — this instance understands version {BundleFormat.CurrentSchemaVersion}."
            );
            return false;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add($"Item '{item.Name}' has no name.");
            return false;
        }
        return true;
    }

    private static async Task<string> ReadEntryTextAsync(
        ZipArchiveEntry entry,
        CancellationToken ct
    )
    {
        await using System.IO.Stream stream = entry.Open();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(ct);
    }

    // ── Shared delete path (rollback + uninstall) ───────────────────────────────

    private async Task<Result<InstalledBundleDto>> RollbackAsync(
        Guid broadcasterId,
        Guid actorUserId,
        List<(string Type, Guid Id, string Name)> created,
        string? errorMessage,
        string? errorCode,
        CancellationToken ct
    )
    {
        await DeleteCreatedAsync(broadcasterId, actorUserId, created, ct);
        return Result.Failure<InstalledBundleDto>(
            $"Import failed and was rolled back: {errorMessage}",
            errorCode ?? "IMPORT_FAILED"
        );
    }

    /// <summary>
    /// Deletes exactly the given installed entities through their owning module services (soft deletes).
    /// Best-effort per entity: an entity the user already deleted by hand is simply gone.
    /// </summary>
    private async Task DeleteCreatedAsync(
        Guid broadcasterId,
        Guid actorUserId,
        List<(string Type, Guid Id, string Name)> entities,
        CancellationToken ct
    )
    {
        string channelId = broadcasterId.ToString();

        // Commands before pipelines, so nothing ever points at an already-deleted pipeline.
        foreach ((string type, Guid id, string name) in entities.OrderBy(e => DeleteOrder(e.Type)))
        {
            switch (type)
            {
                case BundleFormat.CommandType:
                    string? commandName =
                        name.Length > 0
                            ? name
                            : await _db
                                .Commands.Where(c => c.BroadcasterId == broadcasterId && c.Id == id)
                                .Select(c => c.Name)
                                .FirstOrDefaultAsync(ct);
                    if (commandName is not null)
                        await _commands.DeleteAsync(channelId, commandName, ct);
                    break;
                case BundleFormat.PipelineType:
                    await _pipelines.DeleteAsync(channelId, id, ct);
                    break;
                case BundleFormat.WidgetType:
                    await _widgets.DeleteAsync(channelId, id.ToString(), ct);
                    break;
                case BundleFormat.SoundType:
                    await _sounds.DeleteAsync(broadcasterId, id, actorUserId, ct);
                    break;
                case BundleFormat.CustomDataSourceType:
                    await _dataSources.DeleteAsync(broadcasterId, id, actorUserId, ct);
                    break;
                case BundleFormat.EventResponseType:
                    // Event responses are addressed by EventType; the lazy catalog seed restores the
                    // disabled default row afterwards, so deleting an upserted response is safe.
                    string? eventType =
                        name.Length > 0
                            ? name
                            : await _db
                                .EventResponses.Where(e =>
                                    e.BroadcasterId == broadcasterId && e.Id == id
                                )
                                .Select(e => e.EventType)
                                .FirstOrDefaultAsync(ct);
                    if (eventType is not null)
                        await _eventResponses.DeleteAsync(channelId, eventType, ct);
                    break;
                case BundleFormat.RewardType:
                    await _rewards.DeleteAsync(channelId, id.ToString(), ct);
                    break;
                case BundleFormat.TimerType:
                    await _timers.DeleteAsync(channelId, id, ct);
                    break;
                case BundleFormat.ChatTriggerType:
                    await _chatTriggers.DeleteAsync(channelId, id, ct);
                    break;
                case BundleFormat.PickListType:
                    await _pickLists.DeleteAsync(broadcasterId, id, ct);
                    break;
                case BundleFormat.CodeScriptType:
                    await _codeScripts.DeleteAsync(id, ct);
                    break;
            }
        }
    }

    // Pipeline-bound item types delete before pipelines, so nothing ever points at a deleted pipeline.
    private static int DeleteOrder(string type) =>
        type switch
        {
            BundleFormat.CommandType => 0,
            BundleFormat.EventResponseType => 0,
            BundleFormat.RewardType => 0,
            BundleFormat.TimerType => 0,
            BundleFormat.ChatTriggerType => 0,
            BundleFormat.PipelineType => 1,
            _ => 2,
        };

    // ── Rename mechanics (D6, policy Rename) ────────────────────────────────────

    /// <summary>
    /// Finds a free name for a conflicting import. Free-text names (pipelines, widgets) get
    /// <c>" (bundle)"</c> / <c>" (bundle N)"</c>; slug names (commands, sounds, data sources) get
    /// <c>"-bundle"</c> / <c>"-bundle-N"</c>. The base is trimmed so the result always fits the column.
    /// </summary>
    private static async Task<string> FreeNameAsync(
        string baseName,
        bool freeText,
        int maxLength,
        Func<string, Task<bool>> existsAsync
    )
    {
        for (int attempt = 1; attempt <= 100; attempt++)
        {
            string suffix = freeText
                ? attempt == 1
                    ? " (bundle)"
                    : $" (bundle {attempt})"
                : attempt == 1
                    ? "-bundle"
                    : $"-bundle-{attempt}";
            int room = maxLength - suffix.Length;
            string trimmed = baseName.Length > room ? baseName[..room] : baseName;
            string candidate = trimmed + suffix;
            if (!await existsAsync(candidate))
                return candidate;
        }
        throw new InvalidOperationException(
            $"No free name could be derived from '{baseName}' after 100 attempts."
        );
    }

    private static InstalledBundleDto ToDto(InstalledBundle row) =>
        new(row.Id, row.Name, row.Source, row.MarketplaceItemId, row.Version, row.CreatedAt);
}
