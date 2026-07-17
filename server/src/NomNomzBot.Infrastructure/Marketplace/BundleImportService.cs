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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Marketplace;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Marketplace.Entities;
using NomNomzBot.Domain.Marketplace.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// Validates and installs bundle ZIPs (marketplace.md §3). Entities are created through the owning module
/// services so each entity's own validation runs; any custom-code content lands DISABLED (D4); the install
/// is recorded as an <see cref="InstalledBundle"/> row so it can later be uninstalled exactly (D6). A
/// mid-import failure rolls back everything the import had already created — an install is all-or-nothing.
/// </summary>
public class BundleImportService : IBundleImportService
{
    private readonly IApplicationDbContext _db;
    private readonly ICommandService _commands;
    private readonly IPipelineService _pipelines;
    private readonly IWidgetService _widgets;
    private readonly ISoundClipService _sounds;
    private readonly ICustomDataSourceService _dataSources;
    private readonly IEventBus _eventBus;

    public BundleImportService(
        IApplicationDbContext db,
        ICommandService commands,
        IPipelineService pipelines,
        IWidgetService widgets,
        ISoundClipService sounds,
        ICustomDataSourceService dataSources,
        IEventBus eventBus
    )
    {
        _db = db;
        _commands = commands;
        _pipelines = pipelines;
        _widgets = widgets;
        _sounds = sounds;
        _dataSources = dataSources;
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

        string channelId = broadcasterId.ToString();
        List<(string Type, Guid Id, string Name)> created = [];
        Dictionary<string, Guid> pipelineIdsByName = [];

        try
        {
            // Pipelines first — commands re-link against them by bundle name.
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

                // D4: a run_code-bearing pipeline always lands disabled, whatever the export says.
                bool hasRunCode = BundleConventions.ContainsRunCode(export.GraphJson);
                CreatePipelineDto request = new()
                {
                    Name = name,
                    Description = export.Description,
                    TriggerKind = export.TriggerKind,
                    IsEnabled = export.IsEnabled && !hasRunCode,
                    GraphJsonCache = export.GraphJson is not null
                        ? System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            export.GraphJson
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

        InstalledBundle row = new()
        {
            BroadcasterId = broadcasterId,
            Name = bundle.Manifest.Metadata.Name,
            Source = "local",
            MarketplaceItemId = null,
            Version = bundle.Manifest.Metadata.Version,
            Author = bundle.Manifest.Metadata.Author,
            License = bundle.Manifest.Metadata.License,
            ManifestJson = BundleConventions.Serialize(bundle.Manifest),
            InstalledEntityIdsJson = BundleConventions.Serialize(installedIds),
            InstalledByUserId = actorUserId,
        };
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
                    default:
                        issues.Add($"Item '{item.Name}' has an unknown type '{item.Type}'.");
                        break;
                }
            }

            // A command's pipeline link must resolve inside the bundle — an import never links to an id.
            HashSet<string> bundledPipelineNames = pipelines
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (CommandExport command in commands)
            {
                if (
                    command.PipelineName is not null
                    && !bundledPipelineNames.Contains(command.PipelineName)
                )
                    issues.Add(
                        $"Command '{command.Name}' depends on pipeline '{command.PipelineName}', which is not in the bundle."
                    );
            }

            IReadOnlyList<string> capabilities = BundleConventions.CollectCapabilities(
                pipelines,
                commands,
                hasWidgets: widgets.Count > 0,
                hasSounds: sounds.Count > 0,
                hasDataSources: dataSources.Count > 0
            );

            return Result.Success(
                new ParsedBundle(
                    manifest,
                    pipelines,
                    commands,
                    widgets,
                    sounds,
                    dataSources,
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
            }
        }
    }

    private static int DeleteOrder(string type) =>
        type switch
        {
            BundleFormat.CommandType => 0,
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
