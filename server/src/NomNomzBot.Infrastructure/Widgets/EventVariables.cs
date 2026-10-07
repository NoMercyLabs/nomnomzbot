// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
