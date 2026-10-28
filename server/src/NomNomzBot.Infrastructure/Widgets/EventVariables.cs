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

using System.Text.Json;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>Reads the variables JSON a stored <c>ChannelEvent</c> keeps in its <c>Data</c> column.</summary>
internal static class EventVariables
{
    // The variables JSON stores every value as text; read a number or a string alike.
    public static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                    value.GetRawText(),
                _ => null,
            }
            : null;
}
