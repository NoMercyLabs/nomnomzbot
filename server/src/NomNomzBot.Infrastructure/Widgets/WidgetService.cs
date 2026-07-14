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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Widgets;

public class WidgetService : IWidgetService
{
    private readonly IApplicationDbContext _db;
    private readonly string _overlayBaseUrl;
    private readonly IEventBus _eventBus;
    private readonly IWidgetBuildService _buildService;
    private readonly TimeProvider _timeProvider;

    public WidgetService(
        IApplicationDbContext db,
        IConfiguration configuration,
        IEventBus eventBus,
        IWidgetBuildService buildService,
        TimeProvider timeProvider
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
        _timeProvider = timeProvider;
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

        return Result.Success(ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl));
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

        List<WidgetDetail> items = widgets
            .Select(w => ToDetail(w, w.Channel.OverlayToken, _overlayBaseUrl))
            .ToList();

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

        return Result.Success(ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl));
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
            (
                await _db
                    .WidgetVersions.Where(v => v.WidgetId == widget.Id)
                    .Select(v => (int?)v.VersionNumber)
                    .MaxAsync(cancellationToken)
            ) ?? 0;
        nextNumber += 1;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        WidgetVersion version = new()
        {
            WidgetId = widget.Id,
            BroadcasterId = broadcasterGuid,
            VersionNumber = nextNumber,
            SourceCode = request.SourceCode,
            BuildStatus = "pending",
            CreatedAt = now,
        };
        _db.WidgetVersions.Add(version);

        Result<WidgetBuildOutput> build = await _buildService.BuildAsync(
            new WidgetBuildInput(widget.Framework, request.SourceCode),
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

        List<WidgetVersionSummary> items = versions.Select(ToVersionSummary).ToList();
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

        return Result.Success(ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl));
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

        List<Guid> activeVersionIds = widgets.Select(w => w.ActiveVersionId!.Value).ToList();
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
                new OverlayWidgetEntry(
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

    public async Task<Result<OverlayBundle>> GetOverlayBundleAsync(
        string overlayToken,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(widgetId, out Guid widgetGuid))
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

    // Derives the render-time trust tier from Source (widgets-overlays.md §1). Fail-closed: custom / anything
    // unexpected maps to `unverified`, never silently to a higher tier.
    private static string ResolveTrustTier(string source) =>
        source switch
        {
            "first_party" => "first_party",
            "verified_gallery" => "verified_community",
            _ => "unverified",
        };

    private static string GenerateCspNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private static WidgetVersionSummary ToVersionSummary(WidgetVersion v) =>
        new(v.Id, v.VersionNumber, v.BuildStatus, v.ContentHash, v.CompiledAt, v.CreatedAt);

    private static WidgetVersionDetail ToVersionDetail(WidgetVersion v) =>
        new(
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

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid widgetId,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "widgets",
                EntityId = widgetId.ToString(),
                Action = action,
            },
            ct
        );

    // The DTO carries nullable values (Dictionary<string, object?>); the store column is non-null
    // (Dictionary<string, object>). Coalesce a null override to "" so a key is never dropped on the round-trip.
    private static Dictionary<string, object> ToSettingsStore(
        Dictionary<string, object?>? settings
    ) =>
        settings?.ToDictionary(k => k.Key, v => v.Value ?? (object)"")
        ?? new Dictionary<string, object>();

    private static WidgetDetail ToDetail(Widget w, string overlayToken, string overlayBaseUrl) =>
        new(
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
            w.UpdatedAt
        );
}
