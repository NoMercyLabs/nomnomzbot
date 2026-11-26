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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Obs.Recovery;

namespace NomNomzBot.Infrastructure.Obs;

/// <summary>
/// A deleted widget takes its OBS browser sources with it, so no scene keeps a source that points at a widget
/// that no longer exists. Without a connected OBS there is nothing to remove.
/// </summary>
public sealed class ObsWidgetSourceRemover(IObsControlService obs)
    : IEventHandler<ChannelConfigChangedEvent>
{
    public async Task HandleAsync(
        ChannelConfigChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (
            @event.Domain != "widgets"
            || @event.Action != "deleted"
            || !Guid.TryParse(@event.EntityId, out Guid deletedWidgetId)
        )
            return;

        Result<IReadOnlyList<ObsBrowserSourceDto>> sources = await obs.GetBrowserSourcesAsync(
            @event.BroadcasterId,
            cancellationToken
        );
        if (sources.IsFailure)
            return;

        foreach (ObsBrowserSourceDto source in sources.Value)
            if (
                ObsWidgetSourceAddress.TryGetWidgetId(source.Url, out Guid widgetId)
                && widgetId == deletedWidgetId
            )
                await obs.RequestAsync(
                    @event.BroadcasterId,
                    new(
                        "RemoveInput",
                        new Dictionary<string, object?> { ["inputName"] = source.Name }
                    ),
                    cancellationToken
                );
    }
}
