// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Widgets.Pipeline;

/// <summary>
/// Supplies the event types the asking channel's widgets listen to, for the <c>widget_event_type</c> picker kind. A
/// widget declares them in its <see cref="Widget.EventSubscriptions"/> (the wildcard <c>*</c> is not an event type, so
/// it is left out). With the step's <c>widget_id</c> sibling set, only that widget's types are offered; with none, the
/// types of every widget of the channel. The saved value is the event type; the secondary text names the widgets that
/// listen to it. Another channel's widgets never appear.
/// </summary>
internal sealed class WidgetEventTypeOptionProvider : IPipelineOptionProvider
{
    private const string WidgetField = "widget_id";

    private readonly IApplicationDbContext _db;

    public WidgetEventTypeOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.WidgetEventType;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IQueryable<Widget> widgets = _db.Widgets.Where(w =>
            w.BroadcasterId == broadcasterId && w.DeletedAt == null
        );
        if (OwnedIdCodec.TryDecode(request.ContextValue(WidgetField), out Guid widgetId))
            widgets = widgets.Where(w => w.Id == widgetId);

        List<Widget> found = await widgets.AsNoTracking().ToListAsync(ct);
        List<PipelineOption> options =
        [
            .. found
                .SelectMany(w =>
                    w.EventSubscriptions.Where(t => t != Widget.WildcardEvent)
                        .Select(t => (Type: t, Widget: w.Name))
                )
                .GroupBy(x => x.Type, StringComparer.Ordinal)
                .Select(g =>
                    PipelineOptionPage.Option(
                        g.Key,
                        g.Key,
                        string.Join(", ", g.Select(x => x.Widget).Distinct().Order())
                    )
                ),
        ];

        return PipelineOptionPage.Of(options, request);
    }
}
