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
