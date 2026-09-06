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
using NomNomzBot.Application.Alerts.Dtos;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Alerts.Entities;

namespace NomNomzBot.Infrastructure.Alerts.Services;

/// <summary>
/// The one alert queue across every platform connection (widgets-overlays.md §1.2). Every enqueue writes the row
/// FIRST — before anything asks whether an overlay is even attached — so a supporter event still produces a
/// queued, dispatchable alert with zero widgets manually installed (the alert system surface is auto-provisioned
/// per channel by <c>SystemWidgetSeedOnOnboardingHandler</c>/<see cref="IWidgetService.EnsureSystemWidgetAsync"/>,
/// never something a streamer clicks "install" on) and zero overlays connected.
/// <para>
/// Delivery is then attempted opportunistically against the SAME <c>IOverlayPresenceRegistry</c> presence check
/// that closed the identical TTS gap: only a live overlay connection moves the row to
/// <see cref="AlertQueueStatus.Delivered"/>; a push into an empty group never does.
/// </para>
/// </summary>
public sealed class AlertQueueService : IAlertQueueService
{
    // Not a gallery item any more (widgets-overlays.md §1.2 header) — the alert surface's stable natural
    // key in the (trimmed) first-party catalogue, matching FirstPartyWidgetCatalogue's "alerts" entry.
    private const string AlertsSurfaceNaturalKey = "alerts";

    // Bounded like RenderedAlertCapture (widget-quality-audit precedent): a per-broadcaster queue this small
    // is pruned on write rather than by a background job.
    private const int MaxEntriesPerBroadcaster = 100;

    private readonly IApplicationDbContext _db;
    private readonly IWidgetService _widgets;
    private readonly IOverlayPresenceRegistry _presence;
    private readonly IWidgetEventNotifier _notifier;
    private readonly TimeProvider _clock;

    public AlertQueueService(
        IApplicationDbContext db,
        IWidgetService widgets,
        IOverlayPresenceRegistry presence,
        IWidgetEventNotifier notifier,
        TimeProvider clock
    )
    {
        _db = db;
        _widgets = widgets;
        _presence = presence;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<Result<AlertQueueEntryDto>> EnqueueAsync(
        Guid broadcasterId,
        string provider,
        string kind,
        object payload,
        CancellationToken cancellationToken = default
    )
    {
        if (broadcasterId == Guid.Empty)
            return Errors.ChannelNotFound<AlertQueueEntryDto>(broadcasterId.ToString());

        AlertQueueEntry entry = new()
        {
            BroadcasterId = broadcasterId,
            Provider = provider,
            Kind = kind,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = AlertQueueStatus.Queued,
        };
        _db.AlertQueueEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        // Get-or-create the auto-provisioned alert system surface (idempotent — the common case is the
        // onboarding seeder already created it) purely to resolve the widget id the presence registry keys
        // its attachment map by. This never surfaces as a gallery "install" to the streamer.
        Result<WidgetDetail> surface = await _widgets.EnsureSystemWidgetAsync(
            broadcasterId.ToString(),
            AlertsSurfaceNaturalKey,
            cancellationToken
        );

        if (surface.IsSuccess && _presence.IsWidgetAttached(broadcasterId, surface.Value.Id))
        {
            await _notifier.SendWidgetEventAsync(
                broadcasterId,
                surface.Value.Id,
                kind,
                payload,
                cancellationToken
            );
            entry.Status = AlertQueueStatus.Delivered;
            entry.DeliveredAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
        }

        await PruneAsync(broadcasterId, cancellationToken);

        return Result.Success(ToDto(entry));
    }

    public async Task<Result<AlertQueueDto>> GetQueueAsync(
        Guid broadcasterId,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        if (broadcasterId == Guid.Empty)
            return Errors.ChannelNotFound<AlertQueueDto>(broadcasterId.ToString());

        List<AlertQueueEntry> entries = await _db
            .AlertQueueEntries.Where(e => e.BroadcasterId == broadcasterId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        Result<WidgetDetail> surface = await _widgets.EnsureSystemWidgetAsync(
            broadcasterId.ToString(),
            AlertsSurfaceNaturalKey,
            cancellationToken
        );
        bool connected =
            surface.IsSuccess && _presence.IsWidgetAttached(broadcasterId, surface.Value.Id);

        return Result.Success(new AlertQueueDto(connected, [.. entries.Select(ToDto)]));
    }

    private async Task PruneAsync(Guid broadcasterId, CancellationToken cancellationToken)
    {
        List<Guid> staleIds = await _db
            .AlertQueueEntries.Where(e => e.BroadcasterId == broadcasterId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(MaxEntriesPerBroadcaster)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
            return;

        await _db
            .AlertQueueEntries.Where(e => staleIds.Contains(e.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static AlertQueueEntryDto ToDto(AlertQueueEntry entry) =>
        new(
            entry.Id,
            entry.Provider,
            entry.Kind,
            entry.PayloadJson,
            entry.Status,
            entry.CreatedAt,
            entry.DeliveredAt
        );
}
