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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

public class WidgetService : IWidgetService
{
    private readonly IApplicationDbContext _db;
    private readonly string _overlayBaseUrl;

    public WidgetService(IApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _overlayBaseUrl = configuration["OverlayBaseUrl"] ?? "http://localhost:8080";
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

        Widget widget = new()
        {
            Id = Guid.NewGuid().ToString(),
            BroadcasterId = broadcasterGuid,
            Name = request.Name,
            Framework = request.Type,
            IsEnabled = true,
            EventSubscriptions = request.EventSubscriptions ?? [],
            Settings =
                request.Settings?.ToDictionary(k => k.Key, v => v.Value ?? (object)"")
                ?? new Dictionary<string, object>(),
        };

        _db.Widgets.Add(widget);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetail(widget, channel.OverlayToken, _overlayBaseUrl));
    }

    public async Task<Result<WidgetDetail>> UpdateAsync(
        string broadcasterId,
        string widgetId,
        UpdateWidgetRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db
            .Widgets.Include(w => w.Channel)
            .FirstOrDefaultAsync(
                w => w.Id == widgetId && w.BroadcasterId == broadcasterGuid,
                cancellationToken
            );

        if (widget is null)
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        if (request.Name is not null)
            widget.Name = request.Name;
        if (request.IsEnabled.HasValue)
            widget.IsEnabled = request.IsEnabled.Value;
        if (request.EventSubscriptions is not null)
            widget.EventSubscriptions = request.EventSubscriptions;
        if (request.Settings is not null)
            widget.Settings = request.Settings.ToDictionary(k => k.Key, v => v.Value ?? (object)"");

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetail(widget, widget.Channel.OverlayToken, _overlayBaseUrl));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == widgetId && w.BroadcasterId == broadcasterGuid,
            cancellationToken
        );

        if (widget is null)
            return Result.Failure($"Widget '{widgetId}' was not found.", "NOT_FOUND");

        _db.Widgets.Remove(widget);
        await _db.SaveChangesAsync(cancellationToken);

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
                new PagedList<WidgetDetail>([], 0, pagination.Page, pagination.PageSize)
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
            new PagedList<WidgetDetail>(items, total, pagination.Page, pagination.PageSize)
        );
    }

    public async Task<Result<WidgetDetail>> GetAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.NotFound<WidgetDetail>("Widget", widgetId);

        Widget? widget = await _db
            .Widgets.Include(w => w.Channel)
            .FirstOrDefaultAsync(
                w => w.Id == widgetId && w.BroadcasterId == broadcasterGuid,
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

    private static WidgetDetail ToDetail(Widget w, string overlayToken, string overlayBaseUrl) =>
        new(
            w.Id,
            w.Name,
            w.Framework,
            w.IsEnabled,
            $"{overlayBaseUrl}/overlay?widgetId={w.Id}&token={overlayToken}",
            w.Settings.ToDictionary(k => k.Key, v => (object?)v.Value),
            w.EventSubscriptions,
            w.CreatedAt,
            w.UpdatedAt
        );
}
