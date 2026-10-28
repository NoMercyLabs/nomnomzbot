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

using System.Web;

namespace NomNomzBot.Infrastructure.Obs.Recovery;

/// <summary>
/// Reads the widget id out of a browser-source address that points at the bot's widget page
/// (<c>/overlay?widgetId=...&amp;token=...</c>, see <c>WidgetService.BuildOverlayUrl</c>). The host is not
/// compared: the address a streamer pastes into OBS carries whatever origin they reach the bot on, so the page
/// path and the widget id decide.
/// </summary>
internal static class ObsWidgetSourceAddress
{
    private const string WidgetPagePath = "/overlay";

    public static bool TryGetWidgetId(string address, out Guid widgetId)
    {
        widgetId = Guid.Empty;
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
            return false;

        string path = uri.AbsolutePath.TrimEnd('/');
        if (!path.Equals(WidgetPagePath, StringComparison.OrdinalIgnoreCase))
            return false;

        string? value = HttpUtility.ParseQueryString(uri.Query)["widgetId"];
        return Guid.TryParse(value, out widgetId);
    }
}
