// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Obs.Recovery;

/// <summary>
/// The recovery pass (see <see cref="IObsBrowserSourceRecoveryService"/>). OBS says which browser sources are
/// loaded and which address each holds; the overlay presence registry says which widgets have a live hub
/// connection. A source gets one <c>refreshnocache</c> press only when all of these hold: its widget is enabled in the
/// bot and belongs to the channel, OBS has the source switched on in a scene and loaded, and the widget has no live
/// connection. The pass is read-only apart from that press: it never enables, shows or unhides a source in OBS and
/// never changes a widget.
/// </summary>
public sealed class ObsBrowserSourceRecoveryService : IObsBrowserSourceRecoveryService
{
    private readonly IObsControlService _obs;
    private readonly IApplicationDbContext _db;
    private readonly IOverlayPresenceRegistry _presence;
    private readonly ILogger<ObsBrowserSourceRecoveryService> _logger;

    public ObsBrowserSourceRecoveryService(
        IObsControlService obs,
        IApplicationDbContext db,
        IOverlayPresenceRegistry presence,
        ILogger<ObsBrowserSourceRecoveryService> logger
    )
    {
        _obs = obs;
        _db = db;
        _presence = presence;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<string>>> RecoverAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsBrowserSourceDto>> listed = await _obs.GetBrowserSourcesAsync(
            broadcasterId,
            ct
        );
        if (listed.IsFailure)
            return Result.Failure<IReadOnlyList<string>>(
                listed.ErrorMessage,
                listed.ErrorCode,
                listed.ErrorDetail
            );

        List<(string Name, Guid WidgetId)> loaded = [];
        foreach (ObsBrowserSourceDto source in listed.Value)
        {
            if (
                !source.IsLoaded
                || !source.IsEnabledInScene
                || !ObsWidgetSourceAddress.TryGetWidgetId(source.Url, out Guid id)
            )
                continue;

            loaded.Add((source.Name, id));
        }

        if (loaded.Count == 0)
            return Result.Success<IReadOnlyList<string>>([]);

        List<Guid> candidateIds = [.. loaded.Select(s => s.WidgetId).Distinct()];
        List<Guid> ownedIds = await _db
            .Widgets.Where(w =>
                w.BroadcasterId == broadcasterId && candidateIds.Contains(w.Id) && w.IsEnabled
            )
            .Select(w => w.Id)
            .ToListAsync(ct);

        // Presence is per widget, not per source: a widget with a live connection leaves all its sources alone.
        HashSet<Guid> dead =
        [
            .. ownedIds.Where(id => !_presence.IsWidgetAttached(broadcasterId, id)),
        ];

        List<string> refreshed = [];
        foreach ((string name, Guid widgetId) in loaded)
        {
            if (!dead.Contains(widgetId))
                continue;

            Result press = await _obs.RefreshBrowserAsync(broadcasterId, name, ct);
            if (press.IsFailure)
            {
                _logger.LogWarning(
                    "OBS refused to reload browser source {Source} for channel {ChannelId}: {Error}",
                    name,
                    broadcasterId,
                    press.ErrorMessage
                );
                continue;
            }

            refreshed.Add(name);
        }

        if (refreshed.Count > 0)
            _logger.LogInformation(
                "Reloaded {Count} OBS browser source(s) with no widget connection for channel {ChannelId}: {Sources}",
                refreshed.Count,
                broadcasterId,
                string.Join(", ", refreshed)
            );

        return Result.Success<IReadOnlyList<string>>(refreshed);
    }
}
