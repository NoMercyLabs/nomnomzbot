// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Widgets;

public class WidgetService : IWidgetService
{
    private readonly IApplicationDbContext _db;
    private readonly string _overlayBaseUrl;
    private readonly IEventBus _eventBus;
    private readonly IWidgetBuildService _buildService;
    private readonly IWidgetSettingsSchemaProvider _settingsSchemas;
    private readonly TimeProvider _timeProvider;
    private readonly IMusicService _musicService;
    private readonly IScriptStorageService _scriptStorage;
    private readonly IPipelineStepReferenceScanner _stepReferences;
    private readonly IOverlayPresenceRegistry _presence;

    public WidgetService(
        IApplicationDbContext db,
        IConfiguration configuration,
        IEventBus eventBus,
        IWidgetBuildService buildService,
        IWidgetSettingsSchemaProvider settingsSchemas,
        TimeProvider timeProvider,
        IMusicService musicService,
        IScriptStorageService scriptStorage,
        IPipelineStepReferenceScanner stepReferences,
        IOverlayPresenceRegistry presence
    )
    {
        _db = db;
        // The overlay host page is served by this API (OverlayHostController) — the widget URL points
        // at the bot's own base URL unless an operator explicitly fronts overlays elsewhere.
        _overlayBaseUrl =
            configuration["OverlayBaseUrl"]
            ?? configuration["App:BaseUrl"]
            ?? "http://localhost:5080";
        _eventBus = eventBus;
        _buildService = buildService;
        _settingsSchemas = settingsSchemas;
        _timeProvider = timeProvider;
        _musicService = musicService;
        _scriptStorage = scriptStorage;
        _stepReferences = stepReferences;
        _presence = presence;
    }

    public async Task<Result<WidgetDetail>> CreateAsync(
        string broadcasterId,
        CreateWidgetRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        // Create always produces a self-authored `custom` widget — the only Source create can yield (gallery
        // installs go through InstallFromGalleryAsync). No version exists yet: the authored source arrives on the
        // first compile-on-save, which appends the widget's first WidgetVersion and sets ActiveVersionId.
        Widget widget = new()
        {
            BroadcasterId = broadcasterGuid,
            Name = request.Name,
            Description = request.Description,
            Framework = request.Framework,
            Source = "custom",
            IsEnabled = true,
            EventSubscriptions = request.EventSubscriptions ?? [],
            Settings = ToSettingsStore(request.Settings),
        };

        _db.Widgets.Add(widget);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterGuid, widget.Id, "created", cancellationToken);

        return Result.Success(ToDetail(widget, channel.OverlayToken, _overlayBaseUrl));
    }

    public async Task<Result<WidgetDetail>> CloneToEditAsync(
        string broadcasterId,
        CloneWidgetRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        // Exactly one fork source.
        bool hasGallery = request.GalleryItemId.HasValue;
        bool hasInstalled = request.InstalledWidgetId.HasValue;
        if (hasGallery == hasInstalled)
            return Result.Failure<WidgetDetail>(
                "Provide exactly one of galleryItemId or installedWidgetId to clone.",
                "WIDGET_CLONE_SOURCE_INVALID"
            );

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        // Resolve the fork source — a verified gallery item or one of the caller's installed widgets — into the
        // fields the clone copies. A clone is always a fully-detached custom widget: it takes only the source's shape
        // + code, never a link back (GalleryItemId stays null; a gallery item's InstallCount is untouched).
        string forkName;
        string? forkDescription;
        string forkFramework;
        List<string> forkSubscriptions;
        Dictionary<string, object> forkSettings;
        string forkSource;

        if (hasGallery)
        {
            Guid galleryItemId = request.GalleryItemId!.Value;
            WidgetGalleryItem? item = await _db.WidgetGalleryItems.FirstOrDefaultAsync(
                i => i.Id == galleryItemId,
                cancellationToken
            );
            if (item is null)
                return Errors.NotFound<WidgetDetail>("WidgetGalleryItem", galleryItemId.ToString());
            if (item.ReviewStatus != "verified")
                return Result.Failure<WidgetDetail>(
                    "This gallery item is not verified and cannot be cloned.",
                    "WIDGET_GALLERY_ITEM_NOT_VERIFIED"
                );
            if (item.SourceCode is null)
                return Result.Failure<WidgetDetail>(
                    "This gallery item has no source to clone.",
                    "WIDGET_NO_SOURCE"
                );
            forkName = item.Name;
            forkDescription = item.Description;
            forkFramework = item.Framework;
            forkSubscriptions = [.. item.DefaultEventSubscriptions];
            forkSettings = new(item.DefaultSettings);
            forkSource = item.SourceCode;
        }
        else
        {
            Guid sourceWidgetId = request.InstalledWidgetId!.Value;
            Widget? source = await _db.Widgets.FirstOrDefaultAsync(
                w => w.Id == sourceWidgetId && w.BroadcasterId == broadcasterGuid,
                cancellationToken
            );
            if (source is null)
                return Errors.NotFound<WidgetDetail>("Widget", sourceWidgetId.ToString());

            // Copy the LATEST authored source (what the editor shows), then recompile it into the clone.
            WidgetVersion? latest = await _db
                .WidgetVersions.Where(v => v.WidgetId == sourceWidgetId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest?.SourceCode is null)
                return Result.Failure<WidgetDetail>(
                    "The widget has no source to clone yet — compile it first.",
                    "WIDGET_NO_SOURCE"
                );
            forkName = source.Name;
            forkDescription = source.Description;
            forkFramework = source.Framework;
            forkSubscriptions = [.. source.EventSubscriptions];
            forkSettings = new(source.Settings);
            forkSource = latest.SourceCode;
        }

        string clonedName = "Copy of " + forkName;
        if (clonedName.Length > 255)
            clonedName = clonedName[..255];

        Widget clone = new()
        {
            BroadcasterId = broadcasterGuid,
            Name = clonedName,
            Description = forkDescription,
            Framework = forkFramework,
            Source = "custom", // a self-authored fork is always custom (=> unverified trust tier), fully detached
            IsEnabled = true,
            EventSubscriptions = forkSubscriptions,
            Settings = forkSettings,
        };
        _db.Widgets.Add(clone);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterGuid, clone.Id, "created", cancellationToken);

        // Compile the copied source so the clone is immediately live + independently editable.
        Result<WidgetVersionDetail> compiled = await CompileAsync(
            broadcasterId,
            clone.Id.ToString(),
            new() { SourceCode = forkSource },
            cancellationToken
        );
        if (compiled.IsFailure)
            return Result.Failure<WidgetDetail>(
                compiled.ErrorMessage ?? "The cloned widget failed to compile.",
                compiled.ErrorCode ?? "WIDGET_BUILD_FAILED"
            );

        return await GetAsync(broadcasterId, clone.Id.ToString(), cancellationToken);
    }

    public async Task<Result<WidgetDetail>> EnsureSystemWidgetAsync(
        string broadcasterId,
        string galleryNaturalKey,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        WidgetGalleryItem? item = await _db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.NaturalKey == galleryNaturalKey,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<WidgetDetail>("WidgetGalleryItem", galleryNaturalKey);

        // Get-or-create (widgets-overlays.md §1.2): a system surface is "provisioned for every channel at
        // channel creation (and on first use if missing)" — this is the "on first use" leg, called from the
        // owner page (e.g. the TTS page) rather than from every channel-creation call site. Already-installed
        // is the common case and must never re-install / re-bump the gallery item's InstallCount.
        Widget? existing = await _db.Widgets.FirstOrDefaultAsync(
            w => w.BroadcasterId == broadcasterGuid && w.GalleryItemId == item.Id,
            cancellationToken
        );
        if (existing is not null)
            return await GetAsync(broadcasterId, existing.Id.ToString(), cancellationToken);

        return await InstallFromGalleryAsync(broadcasterId, item.Id.ToString(), cancellationToken);
    }

    public async Task<Result<WidgetDetail>> InstallFromGalleryAsync(
        string broadcasterId,
        string galleryItemId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);
        if (!Guid.TryParse(galleryItemId, out Guid galleryItemGuid))
            return Errors.NotFound<WidgetDetail>("WidgetGalleryItem", galleryItemId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<WidgetDetail>(broadcasterId);

        WidgetGalleryItem? item = await _db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == galleryItemGuid,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<WidgetDetail>("WidgetGalleryItem", galleryItemId);

        // Only a verified item installs. (On SaaS an item must also be AvailableInSaaS — first-party items always
        // are; the SaaS-availability gate lands with the community-submission slice + the deployment-profile check.)
        if (item.ReviewStatus != "verified")
            return Result.Failure<WidgetDetail>(
                "This gallery item is not verified and cannot be installed.",
                "WIDGET_GALLERY_ITEM_NOT_VERIFIED"
            );
        if (item.SourceCode is null)
            return Result.Failure<WidgetDetail>(
                "This gallery item has no source to install.",
                "WIDGET_NO_SOURCE"
            );

        // The installed widget's Source drives its (derived, never-stored) trust tier: a first-party item installs as
        // first_party, a verified-community item as verified_gallery. GalleryItemId links it back to the catalogue.
        string source = item.TrustTier == "first_party" ? "first_party" : "verified_gallery";

        Widget widget = new()
        {
            BroadcasterId = broadcasterGuid,
            Name = item.Name,
            Description = item.Description,
            Framework = item.Framework,
            Source = source,
            GalleryItemId = item.Id,
            InstalledSourceRevision = item.SourceRevision,
            IsEnabled = true,
            EventSubscriptions = [.. item.DefaultEventSubscriptions],
            Settings = new(item.DefaultSettings),
        };
        await StampPlatformSourceAsync(widget, item, cancellationToken);
        _db.Widgets.Add(widget);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterGuid, widget.Id, "created", cancellationToken);

        // Compile the shipped source into the first version so the install is immediately live.
        Result<WidgetVersionDetail> compiled = await CompileAsync(
            broadcasterId,
            widget.Id.ToString(),
            new() { SourceCode = item.SourceCode },
            cancellationToken
        );
        if (compiled.IsFailure)
            return Result.Failure<WidgetDetail>(
                compiled.ErrorMessage ?? "The installed widget failed to compile.",
                compiled.ErrorCode ?? "WIDGET_BUILD_FAILED"
            );

        // Atomic increment on the shared gallery row (SET InstallCount = InstallCount + 1 evaluated at
        // write time), not the prior in-memory `item.InstallCount += 1` + SaveChanges — concurrent
        // installs of the same gallery item (by different channels) were silently losing counts under
        // that read-modify-write. Mirrors S004's ExecuteUpdateAsync mechanism.
        Guid installedGalleryItemId = item.Id;
        await _db
            .WidgetGalleryItems.Where(i => i.Id == installedGalleryItemId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(i => i.InstallCount, i => i.InstallCount + 1),
                cancellationToken
            );

        return await GetAsync(broadcasterId, widget.Id.ToString(), cancellationToken);
    }

    public async Task<Result<WidgetDetail>> UpdateFromGalleryAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);
        if (widget.GalleryItemId is null)
            return Result.Failure<WidgetDetail>(
                "This widget was not installed from the gallery and has nothing to update from.",
                "WIDGET_NOT_GALLERY_LINKED"
            );

        WidgetGalleryItem? item = await _db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == widget.GalleryItemId.Value,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<WidgetDetail>(
                "WidgetGalleryItem",
                widget.GalleryItemId.Value.ToString()
            );
        if (item.SourceCode is null)
            return Result.Failure<WidgetDetail>(
                "This gallery item has no source to update from.",
                "WIDGET_NO_SOURCE"
            );

        // Compile-on-save with the gallery's current source, exactly like an authored save — a new WidgetVersion,
        // never an edit to the streamer's history. Settings/subscriptions the streamer has since customized are
        // left untouched; only the source (and the revision it is pinned to) moves.
        Result<WidgetVersionDetail> compiled = await CompileAsync(
            broadcasterId,
            widgetId,
            new() { SourceCode = item.SourceCode },
            cancellationToken
        );
        if (compiled.IsFailure)
            return Result.Failure<WidgetDetail>(
                compiled.ErrorMessage ?? "The updated widget failed to compile.",
                compiled.ErrorCode ?? "WIDGET_BUILD_FAILED"
            );

        widget.InstalledSourceRevision = item.SourceRevision;
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(broadcasterId, widgetId, cancellationToken);
    }

    public async Task<Result<WidgetDetail>> UpdateAsync(
        string broadcasterId,
        string widgetId,
        UpdateWidgetRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db
            .Widgets.Include(w => w.Channel)
            .FirstOrDefaultAsync(
                w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
                cancellationToken
            );

        if (widget is null)
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        if (request.Name is not null)
            widget.Name = request.Name;
        if (request.Description is not null)
            widget.Description = request.Description;
        if (request.IsEnabled.HasValue)
            widget.IsEnabled = request.IsEnabled.Value;
        if (request.EventSubscriptions is not null)
            widget.EventSubscriptions = request.EventSubscriptions;
        if (request.Settings is not null)
            widget.Settings = ToSettingsStore(request.Settings);

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterGuid, widget.Id, "updated", cancellationToken);

        int? galleryRevision = await GetGalleryRevisionAsync(
            widget.GalleryItemId,
            cancellationToken
        );
        return Result.Success(
            ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl, galleryRevision)
        );
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );

        if (widget is null)
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        _db.Widgets.Remove(widget);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(broadcasterGuid, widget.Id, "deleted", cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Counts what breaks if this widget goes: its stored versions follow a real FK (<c>WidgetVersion.WidgetId</c>),
    /// but pipeline steps name it only inside <c>PipelineStep.ConfigJson</c>, so those are scanned rather than joined.
    /// </summary>
    public async Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Result<BlastRadiusDto>.Failure(
                $"Widget '{widgetId}' was not found.",
                "NOT_FOUND"
            );

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Result<BlastRadiusDto>.Failure(
                $"Widget '{widgetId}' was not found.",
                "NOT_FOUND"
            );

        int versionCount = await _db.WidgetVersions.CountAsync(
            v => v.BroadcasterId == broadcasterGuid && v.WidgetId == widgetGuid,
            cancellationToken
        );

        Result<PipelineStepReferenceScan> scan = await _stepReferences.ScanAsync(
            broadcasterGuid,
            ["widget_id", "widget"],
            [widgetGuid.ToString()],
            ct: cancellationToken
        );
        if (scan.IsFailure)
            return Result<BlastRadiusDto>.Failure(
                scan.ErrorMessage ?? "The reference scan failed.",
                scan.ErrorCode ?? "SCAN_FAILED"
            );

        List<BlastRadiusCategoryDto> categories = [];
        if (versionCount > 0)
            categories.Add(new(BlastRadiusCategoryKeys.WidgetVersions, versionCount, []));
        if (scan.Value.MatchCount > 0)
            categories.Add(
                new(
                    BlastRadiusCategoryKeys.PipelineSteps,
                    scan.Value.MatchCount,
                    scan.Value.PipelineNames
                )
            );

        return Result<BlastRadiusDto>.Success(new(categories, scan.Value.IsMinimum));
    }

    public async Task<Result<PagedList<WidgetDetail>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Result.Success(
                new PagedList<WidgetDetail>([], pagination.Page, pagination.PageSize, 0)
            );

        IQueryable<Widget> query = _db
            .Widgets.Include(w => w.Channel)
            .Where(w => w.BroadcasterId == broadcasterGuid);

        int total = await query.CountAsync(cancellationToken);

        List<Widget> widgets = await query
            .OrderBy(w => w.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<Guid> linkedGalleryItemIds =
        [
            .. widgets
                .Where(w => w.GalleryItemId is not null)
                .Select(w => w.GalleryItemId!.Value)
                .Distinct(),
        ];
        Dictionary<Guid, int> galleryRevisions = await _db
            .WidgetGalleryItems.Where(i => linkedGalleryItemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.SourceRevision, cancellationToken);

        List<WidgetDetail> items =
        [
            .. widgets.Select(w =>
                ToDetail(
                    w,
                    w.Channel.OverlayToken,
                    _overlayBaseUrl,
                    w.GalleryItemId is { } gid && galleryRevisions.TryGetValue(gid, out int rev)
                        ? rev
                        : null
                )
            ),
        ];

        return Result.Success(
            new PagedList<WidgetDetail>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<WidgetDetail>> GetAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db
            .Widgets.Include(w => w.Channel)
            .FirstOrDefaultAsync(
                w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
                cancellationToken
            );

        if (widget is null)
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        int? galleryRevision = await GetGalleryRevisionAsync(
            widget.GalleryItemId,
            cancellationToken
        );
        return Result.Success(
            ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl, galleryRevision)
        );
    }

    public async Task<Result<WidgetDetail>> GetByTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == token,
            cancellationToken
        );

        if (channel is null)
            return Result.Failure<WidgetDetail>(
                "No channel found for the provided token.",
                "NOT_FOUND"
            );

        Widget? widget = await _db
            .Widgets.Where(w => w.BroadcasterId == channel.Id && w.IsEnabled)
            .OrderBy(w => w.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (widget is null)
            return Result.Failure<WidgetDetail>(
                "No enabled widget found for the provided token.",
                "NOT_FOUND"
            );

        return Result.Success(ToDetail(widget, channel.OverlayToken, _overlayBaseUrl));
    }

    public async Task<Result<WidgetSettingsSchema>> GetSettingsSchemaAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetSettingsSchema>("Widget", widgetId);

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Errors.NotFound<WidgetSettingsSchema>("Widget", widgetId);

        // A first-party widget is installed from a gallery item whose NaturalKey IS its widget key (alerts,
        // chat_box, …); that key selects the authored schema. A self-authored `custom` widget carries no gallery
        // link (and no first-party key), so it has no typed schema — it is configured through the code editor.
        string? naturalKey = widget.GalleryItemId is { } galleryItemId
            ? await _db
                .WidgetGalleryItems.Where(item => item.Id == galleryItemId)
                .Select(item => item.NaturalKey)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        WidgetSettingsSchema? schema = naturalKey is not null
            ? _settingsSchemas.GetByKey(naturalKey)
            : null;
        if (schema is null)
            return Result.Failure<WidgetSettingsSchema>(
                "This widget has no typed settings schema — configure it through the code editor.",
                "WIDGET_NO_SETTINGS_SCHEMA"
            );

        return Result.Success(schema);
    }

    public async Task<Result<WidgetVersionDetail>> CompileAsync(
        string broadcasterId,
        string widgetId,
        CompileWidgetRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetVersionDetail>("Widget", widgetId);

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );

        if (widget is null)
            return Errors.NotFound<WidgetVersionDetail>("Widget", widgetId);

        // Append the next version (append-only: corrections are new versions, never edits). A failed build is a
        // persisted `error` row, so the history is a complete, tamper-evident record of every save.
        int nextNumber =
            await _db
                .WidgetVersions.Where(v => v.WidgetId == widget.Id)
                .Select(v => (int?)v.VersionNumber)
                .MaxAsync(cancellationToken)
            ?? 0;
        nextNumber += 1;

        // Wrap the authored source into a one-file project (dev-platform.md §4.2) — single-file authoring stays a
        // FilesJson with one entry — and persist the file set + manifest alongside the legacy SourceCode. The build
        // now consumes the project (multi-file capable), so a later editor can save extra files without a shape change.
        (Dictionary<string, string> files, ProjectManifest manifest) = ProjectScaffold.SingleFile(
            "widget",
            widget.Framework,
            request.SourceCode
        );

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        WidgetVersion version = new()
        {
            WidgetId = widget.Id,
            BroadcasterId = broadcasterGuid,
            VersionNumber = nextNumber,
            SourceCode = request.SourceCode,
            FilesJson = ProjectJson.SerializeFiles(files),
            ManifestJson = ProjectJson.SerializeManifest(manifest),
            BuildStatus = "pending",
            CreatedAt = now,
        };
        _db.WidgetVersions.Add(version);

        Result<WidgetBuildOutput> build = await _buildService.BuildAsync(
            new(manifest, files),
            cancellationToken
        );

        if (build.IsSuccess)
        {
            version.BuildStatus = "success";
            version.CompiledBundle = build.Value.CompiledBundle;
            version.ContentHash = build.Value.ContentHash;
            version.BuildLog = build.Value.BuildLog;
            version.CompiledAt = now;
            widget.ActiveVersionId = version.Id;
            await _db.SaveChangesAsync(cancellationToken);

            await _eventBus.PublishAsync(
                new WidgetBuildSucceededEvent
                {
                    BroadcasterId = broadcasterGuid,
                    WidgetId = widget.Id,
                    VersionId = version.Id,
                    VersionNumber = version.VersionNumber,
                    ContentHash = build.Value.ContentHash,
                },
                cancellationToken
            );
        }
        else
        {
            version.BuildStatus = "error";
            version.BuildError = build.ErrorMessage;
            version.BuildLog = build.ErrorMessage;
            await _db.SaveChangesAsync(cancellationToken);

            await _eventBus.PublishAsync(
                new WidgetBuildFailedEvent
                {
                    BroadcasterId = broadcasterGuid,
                    WidgetId = widget.Id,
                    VersionId = version.Id,
                    VersionNumber = version.VersionNumber,
                    BuildError = build.ErrorMessage ?? "The widget build failed.",
                },
                cancellationToken
            );
        }

        return Result.Success(ToVersionDetail(version));
    }

    public async Task<Result<ProjectDto>> GetProjectAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<ProjectDto>("Widget", widgetId);

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Errors.NotFound<ProjectDto>("Widget", widgetId);

        // The editor opens the LATEST authored version (what "compile-on-save" last wrote, success or not), so the
        // author resumes from their most recent save rather than the currently-live one.
        WidgetVersion? latest = await _db
            .WidgetVersions.Where(v => v.WidgetId == widgetGuid)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return Result.Failure<ProjectDto>(
                "This widget has no saved project yet — compile a first version.",
                "NOT_FOUND"
            );

        return Result.Success(ToProjectDto(latest, widget.Framework));
    }

    public async Task<Result<WidgetVersionDetail>> SaveProjectAsync(
        string broadcasterId,
        string widgetId,
        ProjectDto project,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<WidgetVersionDetail>("Widget", widgetId);

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Errors.NotFound<WidgetVersionDetail>("Widget", widgetId);

        ProjectManifest manifest = project.Manifest.ToManifest();

        // The trust boundary (dev-platform.md §4.2): re-build the submitted project server-side rather than trust any
        // client bundle. BuildAsync runs the entry-exists, path-traversal, and dependency-allowlist guards (phase 3)
        // AND the bundler — one call covers validation + compile. A failure means NOTHING is persisted (append-only
        // history stays a record of real saves, not rejected attempts), and the reason is surfaced to the editor.
        Result<WidgetBuildOutput> build = await _buildService.BuildAsync(
            new(manifest, project.Files),
            cancellationToken
        );
        if (build.IsFailure)
            return Result.Failure<WidgetVersionDetail>(
                build.ErrorMessage ?? "The widget project failed to build.",
                MapProjectBuildFailureCode(build.ErrorCode)
            );

        int nextNumber =
            await _db
                .WidgetVersions.Where(v => v.WidgetId == widget.Id)
                .Select(v => (int?)v.VersionNumber)
                .MaxAsync(cancellationToken)
            ?? 0;
        nextNumber += 1;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        WidgetVersion version = new()
        {
            WidgetId = widget.Id,
            BroadcasterId = broadcasterGuid,
            VersionNumber = nextNumber,
            // The legacy single-source column keeps the entry file's content so consumers that read SourceCode still work.
            SourceCode = project.Files[manifest.Entry],
            FilesJson = ProjectJson.SerializeFiles(project.Files),
            ManifestJson = ProjectJson.SerializeManifest(manifest),
            BuildStatus = "success",
            CompiledBundle = build.Value.CompiledBundle,
            ContentHash = build.Value.ContentHash,
            BuildLog = build.Value.BuildLog,
            CompiledAt = now,
            CreatedAt = now,
        };
        _db.WidgetVersions.Add(version);

        // Keep the widget's declared framework in lock-step with the saved manifest so the two never drift.
        widget.Framework = manifest.Framework;
        widget.ActiveVersionId = version.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new WidgetBuildSucceededEvent
            {
                BroadcasterId = broadcasterGuid,
                WidgetId = widget.Id,
                VersionId = version.Id,
                VersionNumber = version.VersionNumber,
                ContentHash = build.Value.ContentHash,
            },
            cancellationToken
        );
        await PublishConfigChangedAsync(broadcasterGuid, widget.Id, "updated", cancellationToken);

        return Result.Success(ToVersionDetail(version));
    }

    public async Task<Result<PagedList<WidgetVersionSummary>>> ListVersionsAsync(
        string broadcasterId,
        string widgetId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Errors.NotFound<PagedList<WidgetVersionSummary>>("Widget", widgetId);

        bool owned = await _db.Widgets.AnyAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (!owned)
            return Errors.NotFound<PagedList<WidgetVersionSummary>>("Widget", widgetId);

        IQueryable<WidgetVersion> query = _db.WidgetVersions.Where(v => v.WidgetId == widgetGuid);
        int total = await query.CountAsync(cancellationToken);

        List<WidgetVersion> versions = await query
            .OrderByDescending(v => v.VersionNumber)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<WidgetVersionSummary> items = [.. versions.Select(ToVersionSummary)];
        return Result.Success(
            new PagedList<WidgetVersionSummary>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<WidgetVersionDetail>> GetVersionAsync(
        string broadcasterId,
        string widgetId,
        string versionId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
            || !Guid.TryParse(versionId, out Guid versionGuid)
        )
            return Errors.NotFound<WidgetVersionDetail>("Widget version", versionId);

        bool owned = await _db.Widgets.AnyAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (!owned)
            return Errors.NotFound<WidgetVersionDetail>("Widget", widgetId);

        WidgetVersion? version = await _db.WidgetVersions.FirstOrDefaultAsync(
            v => v.Id == versionGuid && v.WidgetId == widgetGuid,
            cancellationToken
        );
        if (version is null)
            return Errors.NotFound<WidgetVersionDetail>("Widget version", versionId);

        return Result.Success(ToVersionDetail(version));
    }

    public async Task<Result<WidgetDetail>> RollbackAsync(
        string broadcasterId,
        string widgetId,
        string versionId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
            || !Guid.TryParse(versionId, out Guid versionGuid)
        )
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db
            .Widgets.Include(w => w.Channel)
            .FirstOrDefaultAsync(
                w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
                cancellationToken
            );
        if (widget is null)
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        WidgetVersion? target = await _db.WidgetVersions.FirstOrDefaultAsync(
            v => v.Id == versionGuid && v.WidgetId == widgetGuid,
            cancellationToken
        );
        if (target is null)
            return Errors.NotFound<WidgetDetail>("Widget version", versionId);
        if (target.BuildStatus != "success")
            return Result.Failure<WidgetDetail>(
                "Can only roll back to a version that built successfully.",
                "WIDGET_VERSION_NOT_SUCCESSFUL"
            );

        // Re-point at the earlier successful build — no recompile — then cache-bust the live overlay.
        widget.ActiveVersionId = target.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new WidgetBuildSucceededEvent
            {
                BroadcasterId = broadcasterGuid,
                WidgetId = widget.Id,
                VersionId = target.Id,
                VersionNumber = target.VersionNumber,
                ContentHash = target.ContentHash ?? string.Empty,
            },
            cancellationToken
        );

        int? galleryRevision = await GetGalleryRevisionAsync(
            widget.GalleryItemId,
            cancellationToken
        );
        return Result.Success(
            ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl, galleryRevision)
        );
    }

    public async Task<Result> RecordRuntimeErrorAsync(
        string broadcasterId,
        string widgetId,
        string error,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !Guid.TryParse(broadcasterId, out Guid broadcasterGuid)
            || !Guid.TryParse(widgetId, out Guid widgetGuid)
        )
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetGuid && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );
        if (widget is null)
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        widget.LastRuntimeError = error;
        widget.LastRanAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<OverlayManifest>> GetOverlayManifestAsync(
        string overlayToken,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<OverlayManifest>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        // The overlay is anonymous (token-authed, no JWT tenant), so CurrentBroadcasterId is empty and the tenant
        // query filter would hide every row — bypass the filters and scope to the resolved channel + not-deleted
        // explicitly. Only enabled widgets with an active, successfully-built version are served.
        List<Widget> widgets = await _db
            .Widgets.IgnoreQueryFilters()
            .Where(w =>
                w.BroadcasterId == channel.Id
                && w.DeletedAt == null
                && w.IsEnabled
                && w.ActiveVersionId != null
            )
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        List<Guid> activeVersionIds = [.. widgets.Select(w => w.ActiveVersionId!.Value)];
        Dictionary<Guid, WidgetVersion> versions = await _db
            .WidgetVersions.IgnoreQueryFilters()
            .Where(v => activeVersionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        List<OverlayWidgetEntry> entries = [];
        foreach (Widget widget in widgets)
        {
            if (
                !versions.TryGetValue(widget.ActiveVersionId!.Value, out WidgetVersion? version)
                || version.BuildStatus != "success"
                || string.IsNullOrEmpty(version.ContentHash)
            )
                continue;

            entries.Add(
                new(
                    widget.Id,
                    widget.Name,
                    widget.Framework,
                    ResolveTrustTier(widget.Source),
                    $"/api/v1/overlay/bundle/{widget.Id}?token={Uri.EscapeDataString(overlayToken)}&v={version.ContentHash}",
                    version.ContentHash,
                    widget.EventSubscriptions,
                    widget.Settings.ToDictionary(k => k.Key, v => (object?)v.Value)
                )
            );
        }

        return Result.Success(new OverlayManifest(channel.Id, GenerateCspNonce(), entries));
    }

    public async Task<Result<string>> GetSpotifyPlaybackTokenAsync(
        string overlayToken,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<string>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        return await _musicService.GetEmbeddedPlaybackTokenAsync(
            channel.Id.ToString(),
            cancellationToken
        );
    }

    public async Task<Result<OverlayNowPlayingSnapshot?>> GetNowPlayingSnapshotAsync(
        string overlayToken,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<OverlayNowPlayingSnapshot?>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        NowPlaying? nowPlaying = await _musicService.GetNowPlayingAsync(
            channel.Id.ToString(),
            cancellationToken
        );
        if (nowPlaying is null)
            return Result.Success<OverlayNowPlayingSnapshot?>(null);

        return Result.Success<OverlayNowPlayingSnapshot?>(
            new(
                nowPlaying.IsPlaying,
                nowPlaying.TrackName,
                nowPlaying.Artist,
                nowPlaying.ImageUrl,
                nowPlaying.Provider,
                nowPlaying.TrackUri,
                nowPlaying.DurationMs,
                nowPlaying.ProgressMs,
                _timeProvider.GetUtcNow(),
                nowPlaying.RequestedBy
            )
        );
    }

    public async Task<Result<string?>> GetScriptStorageValueAsync(
        string overlayToken,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<string?>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        string? value = await _scriptStorage.GetAsync(channel.Id, key, cancellationToken);
        return Result.Success(value);
    }

    public async Task<Result<IReadOnlyList<MusicQueueItem>>> GetQueueSnapshotAsync(
        string overlayToken,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<IReadOnlyList<MusicQueueItem>>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        MusicQueue queue = await _musicService.GetQueueAsync(
            channel.Id.ToString(),
            cancellationToken
        );
        return Result.Success(queue.Queue);
    }

    public async Task<Result<OverlayBundle>> GetOverlayBundleAsync(
        string overlayToken,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryDecodeWidgetId(widgetId, out Guid widgetGuid))
            return Errors.NotFound<OverlayBundle>("Widget", widgetId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.OverlayToken == overlayToken,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<OverlayBundle>(
                "No channel found for the provided overlay token.",
                "NOT_FOUND"
            );

        Widget? widget = await _db
            .Widgets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                w =>
                    w.Id == widgetGuid
                    && w.BroadcasterId == channel.Id
                    && w.DeletedAt == null
                    && w.IsEnabled
                    && w.ActiveVersionId != null,
                cancellationToken
            );
        if (widget is null)
            return Errors.NotFound<OverlayBundle>("Widget", widgetId);

        WidgetVersion? version = await _db
            .WidgetVersions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                v => v.Id == widget.ActiveVersionId!.Value && v.BuildStatus == "success",
                cancellationToken
            );
        if (version is null || version.CompiledBundle is null)
            return Errors.NotFound<OverlayBundle>("Widget bundle", widgetId);

        return Result.Success(
            new OverlayBundle(
                version.CompiledBundle,
                widget.Framework,
                version.ContentHash ?? string.Empty
            )
        );
    }

    // Decodes the bundle route's widget id, accepting BOTH wire forms a client may hold: the 26-char ULID the JSON
    // API serializes owned ids as (UlidGuidJsonConverter), and the raw UUIDv7 Guid the server-built overlay URL
    // carries. This public, anonymous route reaches the service as a raw string (no model binder), so it mirrors the
    // API-boundary GuidUlidCodec here — ULID first (its fixed 26-char length never collides with any Guid format),
    // then a raw Guid — rather than 404ing a perfectly valid ULID-serialized id like every other widget route accepts.
    private static bool TryDecodeWidgetId(string value, out Guid id)
    {
        if (Ulid.TryParse(value, out Ulid ulid))
        {
            id = ulid.ToGuid();
            return true;
        }

        return Guid.TryParse(value, out id);
    }

    public IReadOnlyList<WidgetTemplate> GetTemplates()
    {
        return WidgetTemplateCatalogue.All;
    }

    // Derives the render-time trust tier from Source (widgets-overlays.md §1). Fail-closed: custom / anything
    // unexpected maps to `unverified`, never silently to a higher tier.
    private static string ResolveTrustTier(string source)
    {
        return source switch
        {
            "first_party" => "first_party",
            "verified_gallery" => "verified_community",
            _ => "unverified",
        };
    }

    private static string GenerateCspNonce()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }

    // Project a stored version's file set + manifest back to the editor's wire shape. A legacy row whose
    // FilesJson/ManifestJson was never backfilled is projected as its one-file scaffold from the compiled SourceCode,
    // so GET always returns a coherent project.
    private static ProjectDto ToProjectDto(WidgetVersion version, string framework)
    {
        Dictionary<string, string>? files = ProjectJson.DeserializeFiles(version.FilesJson);
        ProjectManifest? manifest = ProjectJson.DeserializeManifest(version.ManifestJson);
        if (files is null || manifest is null)
            (files, manifest) = ProjectScaffold.SingleFile(
                "widget",
                framework,
                version.SourceCode ?? string.Empty
            );

        return new(files, ProjectManifestDto.FromManifest(manifest));
    }

    // A project save either persists a successful version or returns a failure — there is no `error` row. Translate
    // the build boundary's coded failures to an app error code the API maps sanely: a bundler/validation problem is a
    // 400 (user input), a missing build tool a 503. The build's message (the reason / build log) is surfaced verbatim.
    private static string MapProjectBuildFailureCode(string? buildErrorCode)
    {
        return buildErrorCode == "WIDGET_BUILD_TOOL_UNAVAILABLE"
            ? "SERVICE_UNAVAILABLE"
            : "VALIDATION_FAILED";
    }

    private static WidgetVersionSummary ToVersionSummary(WidgetVersion v)
    {
        return new(v.Id, v.VersionNumber, v.BuildStatus, v.ContentHash, v.CompiledAt, v.CreatedAt);
    }

    private static WidgetVersionDetail ToVersionDetail(WidgetVersion v)
    {
        return new(
            v.Id,
            v.WidgetId,
            v.VersionNumber,
            v.BuildStatus,
            v.SourceCode,
            v.BuildError,
            v.BuildLog,
            v.ContentHash,
            v.CompiledAt,
            v.CreatedAt
        );
    }

    /// <summary>
    /// Stamps <see cref="Widget.PlatformSourceDefinitionId"/> (+ siblings) when the gallery item being installed
    /// is genuinely backed by the platform-content spine (S-ADMIN-2c-b): a published <c>PlatformContentDefinition</c>
    /// with <c>Kind = widget</c> whose <c>Key</c> equals the item's <see cref="WidgetGalleryItem.NaturalKey"/> —
    /// the same natural-key linking rule the command kind uses, never a name match. Only a first-party item can
    /// carry a <c>NaturalKey</c> (community submissions leave it null), so this is a no-op for a community
    /// install. Leaves the row unstamped (null) when no definition matches, the definition has never been
    /// published, or the key is ambiguous — never guesses.
    /// </summary>
    private async Task StampPlatformSourceAsync(
        Widget widget,
        WidgetGalleryItem item,
        CancellationToken cancellationToken
    )
    {
        if (item.NaturalKey is null)
            return;

        List<PlatformContentDefinition> matches = await _db
            .PlatformContentDefinitions.Where(d =>
                d.Kind == PlatformContentKinds.Widget
                && d.Key == item.NaturalKey
                && d.RetiredAt == null
            )
            .ToListAsync(cancellationToken);
        if (matches.Count != 1)
            return; // no match, or ambiguous — never guess.

        PlatformContentDefinition definition = matches[0];
        if (definition.CurrentVersionId is not { } currentVersionId)
            return; // drafted but never published — nothing installable to attribute yet.

        PlatformContentVersion? version = await _db.PlatformContentVersions.FirstOrDefaultAsync(
            v => v.Id == currentVersionId,
            cancellationToken
        );
        if (version is null)
            return;

        widget.PlatformSourceDefinitionId = definition.Id;
        widget.PlatformSourceVersion = version.Version;
        widget.PlatformSourceSyncedAt = _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid widgetId,
        string action,
        CancellationToken ct
    )
    {
        return _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "widgets",
                EntityId = widgetId.ToString(),
                Action = action,
            },
            ct
        );
    }

    // The DTO carries nullable values (Dictionary<string, object?>); the store column is non-null
    // (Dictionary<string, object>). Coalesce a null override to "" so a key is never dropped, AND normalize any
    // System.Text.Json.JsonElement (what a value deserialized from the request body actually is) to a plain CLR
    // primitive — otherwise the store/serialize round-trip mangles it into {"ValueKind":...} and the widget's
    // injected window.WIDGET_SETTINGS is unusable (it can't read its own accentColor / durations / toggles).
    private static Dictionary<string, object> ToSettingsStore(Dictionary<string, object?>? settings)
    {
        return settings?.ToDictionary(k => k.Key, v => NormalizeSetting(v.Value))
            ?? new Dictionary<string, object>();
    }

    /// <summary>Coerce a settings value to a plain CLR object graph so it round-trips through any serializer — a
    /// value parsed from JSON arrives as a <see cref="JsonElement"/>, which serializes as its reflected properties
    /// (ValueKind) rather than its value.</summary>
    private static object NormalizeSetting(object? value)
    {
        if (value is not JsonElement element)
            return value ?? "";

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(item => NormalizeSetting(item))
                .ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => NormalizeSetting(property.Value)
                ),
            _ => "",
        };
    }

    private WidgetDetail ToDetail(
        Widget w,
        string overlayToken,
        string overlayBaseUrl,
        int? currentGalleryRevision = null
    )
    {
        return new(
            w.Id,
            w.Name,
            w.Description,
            w.Framework,
            w.Source,
            w.IsEnabled,
            $"{overlayBaseUrl}/overlay?widgetId={w.Id}&token={overlayToken}",
            w.ActiveVersionId,
            w.GalleryItemId,
            w.Settings.ToDictionary(k => k.Key, v => (object?)v.Value),
            w.EventSubscriptions,
            w.LastRuntimeError,
            w.LastRanAt,
            w.CreatedAt,
            w.UpdatedAt,
            w.GalleryItemId is not null
                && currentGalleryRevision is { } rev
                && rev > (w.InstalledSourceRevision ?? 0),
            _presence.IsWidgetAttached(w.BroadcasterId, w.Id)
        );
    }

    /// <summary>The linked gallery item's current <see cref="WidgetGalleryItem.SourceRevision"/>, or null if unlinked.</summary>
    private async Task<int?> GetGalleryRevisionAsync(Guid? galleryItemId, CancellationToken ct) =>
        galleryItemId is null
            ? null
            : await _db
                .WidgetGalleryItems.Where(i => i.Id == galleryItemId.Value)
                .Select(i => (int?)i.SourceRevision)
                .FirstOrDefaultAsync(ct);
}
