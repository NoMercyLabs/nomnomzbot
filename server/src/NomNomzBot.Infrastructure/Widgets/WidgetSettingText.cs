// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>Reads one widget setting as text; settings come back from the JSON column as a string or a JsonElement.</summary>
internal static class WidgetSettingText
{
    public static string? Read(Widget widget, string key)
    {
        if (!widget.Settings.TryGetValue(key, out object? raw))
            return null;

        string? text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
