// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
