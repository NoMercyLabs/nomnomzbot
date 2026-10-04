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
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// Closes the overlay connections that still hold a widget token the widget no longer accepts. A token rotation
/// keeps the old URL working for a grace window, but a page that connected on the old token must not stay live
/// past it, and a second rotation retires the oldest token at once. Single replica, in memory, like the ticket
/// service.
/// </summary>
public sealed class OverlayTokenSweeper(
    OverlayPresenceRegistry presence,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider
)
{
    private sealed record AcceptedTokens(
        Guid Id,
        string OverlayToken,
        string? PreviousOverlayToken,
        DateTime? PreviousOverlayTokenExpiresAt,
        DateTime? DeletedAt
    );

    /// <summary>Aborts every bound connection of one widget (or of all widgets when null) whose token is no longer valid; returns how many.</summary>
    public async Task<int> SweepAsync(Guid? widgetId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OverlayPresenceRegistry.BoundConnection> bound = presence.BoundConnections(
            widgetId
        );
        if (bound.Count == 0)
            return 0;

        Dictionary<Guid, AcceptedTokens> widgets = await LoadWidgetsAsync(
            bound.Select(b => b.WidgetId).Distinct().ToList(),
            cancellationToken
        );
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        int closed = 0;
        foreach (OverlayPresenceRegistry.BoundConnection connection in bound)
        {
            if (IsAccepted(widgets.GetValueOrDefault(connection.WidgetId), connection.Token, now))
                continue;

            connection.Context.Abort();
            closed++;
        }

        return closed;
    }

    // The sweep runs outside any request, so there is no tenant to filter by; a deleted widget is read too, so its
    // connections are closed rather than skipped.
    private async Task<Dictionary<Guid, AcceptedTokens>> LoadWidgetsAsync(
        List<Guid> widgetIds,
        CancellationToken cancellationToken
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        return await db
            .Widgets.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => widgetIds.Contains(w.Id))
            .Select(w => new AcceptedTokens(
                w.Id,
                w.OverlayToken,
                w.PreviousOverlayToken,
                w.PreviousOverlayTokenExpiresAt,
                w.DeletedAt
            ))
            .ToDictionaryAsync(w => w.Id, cancellationToken);
    }

    private static bool IsAccepted(AcceptedTokens? widget, string token, DateTime now)
    {
        if (widget is null || widget.DeletedAt is not null)
            return false;

        if (string.Equals(widget.OverlayToken, token, StringComparison.Ordinal))
            return true;

        return widget.PreviousOverlayTokenExpiresAt > now
            && string.Equals(widget.PreviousOverlayToken, token, StringComparison.Ordinal);
    }
}
