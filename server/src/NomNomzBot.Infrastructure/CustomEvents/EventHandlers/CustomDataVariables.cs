// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.CustomEvents.EventHandlers;

/// <summary>The template variables a custom-data trigger sets: the source name and each field under it.</summary>
internal static class CustomDataVariables
{
    internal static Dictionary<string, string> Build(
        string sourceName,
        IEnumerable<KeyValuePair<string, string>> fields
    )
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["custom.source"] = sourceName,
        };
        foreach ((string key, string value) in fields)
            variables[$"custom.{sourceName}.{key}"] = value;
        return variables;
    }
}
