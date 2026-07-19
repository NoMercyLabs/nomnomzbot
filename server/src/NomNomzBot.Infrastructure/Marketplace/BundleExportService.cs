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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// Builds portable bundle ZIPs from a channel's own content (marketplace.md §3). Every entity maps through
/// the D2 allowlist contracts in <c>Contracts/Marketplace</c> — ids, tenant references, storage keys, and
/// secrets never enter the archive. A command that executes a pipeline pulls that pipeline in automatically
/// so the manifest always carries a complete dependency graph.
/// </summary>
public class BundleExportService : IBundleExportService
{
    private readonly IApplicationDbContext _db;
    private readonly ISoundClipStore _soundStore;

    public BundleExportService(IApplicationDbContext db, ISoundClipStore soundStore)
    {
        _db = db;
        _soundStore = soundStore;
    }

    public async Task<Result<System.IO.Stream>> ExportAsync(
        Guid broadcasterId,
        ExportRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Metadata.Name))
            return Result.Failure<System.IO.Stream>(
                "The bundle needs a name.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrWhiteSpace(request.Metadata.Version))
            return Result.Failure<System.IO.Stream>(
                "The bundle needs a version.",
                "VALIDATION_FAILED"
            );
        if (request.Items.Count == 0)
            return Result.Failure<System.IO.Stream>(
                "Select at least one item to export.",
                "VALIDATION_FAILED"
            );

        // ── Resolve every requested entity (broadcaster-scoped), de-duplicated ──
        List<ExportItemRef> refs = request.Items.Distinct().ToList();

        Dictionary<Guid, Command> commands = [];
        Dictionary<Guid, Pipeline> pipelines = [];
        Dictionary<Guid, Widget> widgets = [];
        Dictionary<Guid, SoundClip> sounds = [];
        Dictionary<Guid, CustomDataSource> dataSources = [];
        Dictionary<Guid, EventResponse> eventResponses = [];
        Dictionary<Guid, Reward> rewards = [];
        Dictionary<Guid, DomainTimer> timers = [];
        Dictionary<Guid, ChatTrigger> chatTriggers = [];
        Dictionary<Guid, PickList> pickLists = [];
        Dictionary<Guid, CodeScript> codeScripts = [];

        foreach (ExportItemRef itemRef in refs)
        {
            Result resolved = itemRef.Type switch
            {
                BundleFormat.CommandType => await ResolveAsync(
                    _db.Commands,
                    broadcasterId,
                    itemRef.Id,
                    commands,
                    ct
                ),
                BundleFormat.PipelineType => await ResolveAsync(
                    _db.Pipelines,
                    broadcasterId,
                    itemRef.Id,
                    pipelines,
                    ct
                ),
                BundleFormat.WidgetType => await ResolveAsync(
                    _db.Widgets,
                    broadcasterId,
                    itemRef.Id,
                    widgets,
                    ct
                ),
                BundleFormat.SoundType => await ResolveAsync(
                    _db.SoundClips,
                    broadcasterId,
                    itemRef.Id,
                    sounds,
                    ct
                ),
                BundleFormat.CustomDataSourceType => await ResolveAsync(
                    _db.CustomDataSources,
                    broadcasterId,
                    itemRef.Id,
                    dataSources,
                    ct
                ),
                BundleFormat.EventResponseType => await ResolveAsync(
                    _db.EventResponses,
                    broadcasterId,
                    itemRef.Id,
                    eventResponses,
                    ct
                ),
                BundleFormat.RewardType => await ResolveAsync(
                    _db.Rewards,
                    broadcasterId,
                    itemRef.Id,
                    rewards,
                    ct
                ),
                BundleFormat.TimerType => await ResolveAsync(
                    _db.Timers,
                    broadcasterId,
                    itemRef.Id,
                    timers,
                    ct
                ),
                BundleFormat.ChatTriggerType => await ResolveAsync(
                    _db.ChatTriggers,
                    broadcasterId,
                    itemRef.Id,
                    chatTriggers,
                    ct
                ),
                BundleFormat.PickListType => await ResolveAsync(
                    _db.PickLists,
                    broadcasterId,
                    itemRef.Id,
                    pickLists,
                    ct
                ),
                BundleFormat.CodeScriptType => await ResolveAsync(
                    _db.CodeScripts,
                    broadcasterId,
                    itemRef.Id,
                    codeScripts,
                    ct
                ),
                _ => Result.Failure(
                    $"Unknown export item type '{itemRef.Type}'.",
                    "VALIDATION_FAILED"
                ),
            };
            if (resolved.IsFailure)
                return resolved.ToTyped<System.IO.Stream>();
        }

        // ── Complete the dependency graph: a pipeline-bound item's pipeline always travels with it ──
        List<Guid?> boundPipelineIds =
        [
            .. commands.Values.Select(c => c.PipelineId),
            .. eventResponses.Values.Select(e => e.PipelineId),
            .. rewards.Values.Select(r => r.PipelineId),
            .. timers.Values.Select(t => t.PipelineId),
            .. chatTriggers.Values.Select(t => t.PipelineId),
        ];
        foreach (Guid? boundId in boundPipelineIds)
        {
            if (boundId is not Guid pipelineId || pipelines.ContainsKey(pipelineId))
                continue;
            Result pulled = await ResolveAsync(
                _db.Pipelines,
                broadcasterId,
                pipelineId,
                pipelines,
                ct
            );
            if (pulled.IsFailure)
                return pulled.ToTyped<System.IO.Stream>();
        }

        // ── Map through the allowlist contracts + write the archive ──
        MemoryStream buffer = new();
        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            List<BundleManifestItem> manifestItems = [];
            HashSet<string> usedSlugs = [];

            // The `pipeline:<name>` re-link/dependency mechanics shared by every pipeline-bound item type.
            string? PipelineNameOf(Guid? pipelineId) =>
                pipelineId is Guid id ? pipelines[id].Name : null;
            static IReadOnlyList<string> PipelineEdge(string? pipelineName) =>
                pipelineName is null ? [] : [$"{BundleFormat.PipelineType}:{pipelineName}"];

            foreach (Pipeline pipeline in pipelines.Values)
            {
                PipelineExport export = new()
                {
                    Name = pipeline.Name,
                    Description = pipeline.Description,
                    TriggerKind = pipeline.TriggerKind,
                    IsEnabled = pipeline.IsEnabled,
                    GraphJson = pipeline.GraphJsonCache,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.PipelineType,
                        pipeline.Name,
                        export,
                        [],
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (Command command in commands.Values)
            {
                string? pipelineName = PipelineNameOf(command.PipelineId);
                CommandExport export = new()
                {
                    Name = command.Name,
                    Tier = command.Tier,
                    MinPermissionLevel = command.MinPermissionLevel,
                    TemplateResponse = command.TemplateResponse,
                    TemplateResponses = command.TemplateResponses,
                    PipelineName = pipelineName,
                    CooldownSeconds = command.CooldownSeconds,
                    CooldownPerUser = command.CooldownPerUser,
                    Description = command.Description,
                    Aliases = command.Aliases,
                    IsEnabled = command.IsEnabled,
                };
                IReadOnlyList<string> dependencies = PipelineEdge(pipelineName);
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.CommandType,
                        command.Name,
                        export,
                        dependencies,
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (Widget widget in widgets.Values)
            {
                string? sourceCode = await _db
                    .WidgetVersions.Where(v => v.WidgetId == widget.Id && v.SourceCode != null)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => v.SourceCode)
                    .FirstOrDefaultAsync(ct);
                WidgetExport export = new()
                {
                    Name = widget.Name,
                    Description = widget.Description,
                    Framework = widget.Framework,
                    Settings = widget.Settings.ToDictionary(kv => kv.Key, object? (kv) => kv.Value),
                    EventSubscriptions = widget.EventSubscriptions,
                    SourceCode = sourceCode,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.WidgetType,
                        widget.Name,
                        export,
                        [],
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (SoundClip sound in sounds.Values)
            {
                Result<System.IO.Stream> audio = await _soundStore.OpenAsync(sound.StorageKey, ct);
                if (audio.IsFailure)
                    return Result.Failure<System.IO.Stream>(
                        $"The audio of sound clip '{sound.Name}' could not be read: {audio.ErrorMessage}",
                        "EXPORT_FAILED"
                    );

                string slug = UniqueSlug(sound.Name, usedSlugs);
                string audioPath = BundleConventions.SoundAudioPath(slug, sound.MimeType);
                await using (System.IO.Stream source = audio.Value)
                await using (System.IO.Stream entry = archive.CreateEntry(audioPath).Open())
                {
                    await source.CopyToAsync(entry, ct);
                }

                SoundExport export = new()
                {
                    Name = sound.Name,
                    DisplayName = sound.DisplayName,
                    MimeType = sound.MimeType,
                    DefaultVolume = sound.DefaultVolume,
                    AudioPath = audioPath,
                };
                string entryPath = BundleConventions.EntryPath(BundleFormat.SoundType, slug);
                await WriteJsonEntryAsync(archive, entryPath, export, ct);
                manifestItems.Add(
                    new BundleManifestItem(BundleFormat.SoundType, sound.Name, entryPath, [])
                );
            }

            foreach (CustomDataSource dataSource in dataSources.Values)
            {
                // D2: the auth secret has no field on the contract — it cannot travel, by construction.
                CustomDataSourceExport export = new()
                {
                    Name = dataSource.Name,
                    DisplayName = dataSource.DisplayName,
                    SourceKind = dataSource.SourceKind,
                    PresetKey = dataSource.PresetKey,
                    EndpointUrl = dataSource.EndpointUrl,
                    FieldMap =
                        BundleConventions.Deserialize<Dictionary<string, string>>(
                            dataSource.FieldMapJson
                        ) ?? [],
                    PollIntervalSeconds = dataSource.PollIntervalSeconds,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.CustomDataSourceType,
                        dataSource.Name,
                        export,
                        [],
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (EventResponse response in eventResponses.Values)
            {
                string? pipelineName = PipelineNameOf(response.PipelineId);
                EventResponseExport export = new()
                {
                    EventType = response.EventType,
                    ResponseType = response.ResponseType,
                    Message = response.Message,
                    PipelineName = pipelineName,
                    Metadata = response.MetadataJson,
                    IsEnabled = response.IsEnabled,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.EventResponseType,
                        response.EventType,
                        export,
                        PipelineEdge(pipelineName),
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (Reward reward in rewards.Values)
            {
                // D2: TwitchRewardId / IsManageable / IsPlatform never travel — an imported reward is a
                // fresh LOCAL definition the target channel pushes to Twitch through its own sync.
                string? pipelineName = PipelineNameOf(reward.PipelineId);
                RewardExport export = new()
                {
                    Title = reward.Title,
                    Description = reward.Description,
                    Response = reward.Response,
                    Cost = reward.Cost ?? 0,
                    TimerDurationSeconds = reward.TimerDurationSeconds,
                    PipelineName = pipelineName,
                    IsEnabled = reward.IsEnabled,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.RewardType,
                        reward.Title,
                        export,
                        PipelineEdge(pipelineName),
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (DomainTimer timer in timers.Values)
            {
                // LastFiredAt / NextMessageIndex are runtime counters (D2) — never exported.
                string? pipelineName = PipelineNameOf(timer.PipelineId);
                TimerExport export = new()
                {
                    Name = timer.Name,
                    Messages = timer.Messages,
                    IntervalMinutes = timer.IntervalMinutes,
                    MinChatActivity = timer.MinChatActivity,
                    FireOnce = timer.FireOnce,
                    PipelineName = pipelineName,
                    IsEnabled = timer.IsEnabled,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.TimerType,
                        timer.Name,
                        export,
                        PipelineEdge(pipelineName),
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (ChatTrigger trigger in chatTriggers.Values)
            {
                string? pipelineName = PipelineNameOf(trigger.PipelineId);
                ChatTriggerExport export = new()
                {
                    Pattern = trigger.Pattern,
                    MatchType = trigger.MatchType,
                    CaseSensitive = trigger.CaseSensitive,
                    Response = trigger.Response,
                    PipelineName = pipelineName,
                    CooldownSeconds = trigger.CooldownSeconds,
                    MinPermissionLevel = trigger.MinPermissionLevel,
                    IsEnabled = trigger.IsEnabled,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.ChatTriggerType,
                        trigger.Pattern,
                        export,
                        PipelineEdge(pipelineName),
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (PickList pickList in pickLists.Values)
            {
                PickListExport export = new()
                {
                    Name = pickList.Name,
                    Description = pickList.Description,
                    Items = pickList.Items,
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.PickListType,
                        pickList.Name,
                        export,
                        [],
                        usedSlugs,
                        ct
                    )
                );
            }

            foreach (CodeScript script in codeScripts.Values)
            {
                // Export the live (current) version's project, falling back to the newest saved version
                // for a script that never published a valid one — the same read GetProjectAsync makes.
                CodeScriptVersion? version = script.CurrentVersionId is Guid currentId
                    ? await _db.CodeScriptVersions.FirstOrDefaultAsync(v => v.Id == currentId, ct)
                    : await _db
                        .CodeScriptVersions.Where(v => v.CodeScriptId == script.Id)
                        .OrderByDescending(v => v.Version)
                        .FirstOrDefaultAsync(ct);
                if (version is null)
                    return Result.Failure<System.IO.Stream>(
                        $"Code script '{script.Name}' has no saved version to export.",
                        "EXPORT_FAILED"
                    );

                Dictionary<string, string>? files = ProjectJson.DeserializeFiles(version.FilesJson);
                ProjectManifest? manifest = ProjectJson.DeserializeManifest(version.ManifestJson);
                if (files is null || manifest is null)
                    (files, manifest) = ProjectScaffold.SingleFile(
                        "script",
                        script.Language,
                        version.SourceCode
                    );

                CodeScriptExport export = new()
                {
                    Name = script.Name,
                    Description = script.Description,
                    Language = script.Language,
                    Files = files,
                    Manifest = new CodeScriptManifestExport(
                        manifest.Entry,
                        manifest.Kind,
                        manifest.Framework,
                        manifest.Dependencies
                    ),
                    DeclaredCapabilities =
                        BundleConventions.Deserialize<List<string>>(
                            version.DeclaredCapabilitiesJson
                        ) ?? [],
                };
                manifestItems.Add(
                    await WriteItemAsync(
                        archive,
                        BundleFormat.CodeScriptType,
                        script.Name,
                        export,
                        [],
                        usedSlugs,
                        ct
                    )
                );
            }

            BundleManifest bundleManifest = new()
            {
                Metadata = request.Metadata,
                Items = manifestItems,
            };
            await WriteJsonEntryAsync(archive, BundleFormat.ManifestEntryName, bundleManifest, ct);
        }

        buffer.Position = 0;
        return Result.Success<System.IO.Stream>(buffer);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<Result> ResolveAsync<TEntity>(
        DbSet<TEntity> set,
        Guid broadcasterId,
        Guid id,
        Dictionary<Guid, TEntity> resolved,
        CancellationToken ct
    )
        where TEntity : class, NomNomzBot.Domain.Platform.ITenantScoped
    {
        if (resolved.ContainsKey(id))
            return Result.Success();

        TEntity? entity = await set.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcasterId && EF.Property<Guid>(e, "Id") == id,
            ct
        );
        if (entity is null)
            return Result.Failure(
                $"{typeof(TEntity).Name} '{id}' was not found on this channel.",
                "NOT_FOUND"
            );

        resolved[id] = entity;
        return Result.Success();
    }

    private static async Task<BundleManifestItem> WriteItemAsync<TExport>(
        ZipArchive archive,
        string type,
        string name,
        TExport export,
        IReadOnlyList<string> dependencies,
        HashSet<string> usedSlugs,
        CancellationToken ct
    )
    {
        string entryPath = BundleConventions.EntryPath(type, UniqueSlug(name, usedSlugs));
        await WriteJsonEntryAsync(archive, entryPath, export, ct);
        return new BundleManifestItem(type, name, entryPath, dependencies);
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryPath,
        T value,
        CancellationToken ct
    )
    {
        await using System.IO.Stream entry = archive.CreateEntry(entryPath).Open();
        await using StreamWriter writer = new(entry);
        await writer.WriteAsync(BundleConventions.Serialize(value).AsMemory(), ct);
    }

    private static string UniqueSlug(string name, HashSet<string> usedSlugs)
    {
        string baseSlug = BundleConventions.Slug(name);
        string slug = baseSlug;
        int suffix = 2;
        while (!usedSlugs.Add(slug))
            slug = $"{baseSlug}-{suffix++}";
        return slug;
    }
}
