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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Obs.Recovery;

namespace NomNomzBot.Infrastructure.Obs;

/// <summary>
/// Saved settings can change the size an overlay is drawn for (the size percent, the number of rows). Every OBS
/// browser source that shows the widget gets the new width and height, so a bigger overlay is never clipped. With
/// OBS not connected nothing happens; the next Add to scene sets the size.
/// </summary>
public sealed class ObsWidgetSourceResizer(IObsControlService obs)
    : IEventHandler<WidgetSettingsChangedEvent>
{
    public async Task HandleAsync(
        WidgetSettingsChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !TryInt(@event.Settings, FirstPartyWidgetCatalogue.DesignWidthSettingKey, out int width)
            || !TryInt(
                @event.Settings,
                FirstPartyWidgetCatalogue.DesignHeightSettingKey,
                out int height
            )
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
                && widgetId == @event.WidgetId
            )
                await obs.RequestAsync(
                    @event.BroadcasterId,
                    new(
                        "SetInputSettings",
                        new Dictionary<string, object?>
                        {
                            ["inputName"] = source.Name,
                            ["inputSettings"] = new Dictionary<string, object?>
                            {
                                ["width"] = width,
                                ["height"] = height,
                            },
                            ["overwrite"] = false,
                        }
                    ),
                    cancellationToken
                );
    }

    private static bool TryInt(
        IReadOnlyDictionary<string, object> settings,
        string key,
        out int value
    )
    {
        value = 0;
        return settings.TryGetValue(key, out object? raw)
            && int.TryParse(raw?.ToString(), out value)
            && value > 0;
    }
}
