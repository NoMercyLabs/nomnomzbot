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
