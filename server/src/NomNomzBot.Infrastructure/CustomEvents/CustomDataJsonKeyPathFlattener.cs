// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// Flattens a parsed JSON document into the leaf key-paths a field-map entry can reference, in the exact
/// <c>JToken.SelectToken</c> syntax the field map already uses (<c>$.data.heartRate</c>, <c>$.items[0].name</c>)
/// — <see cref="CustomDataFieldMapValidator"/> and <c>CustomDataIngestService</c>. Backs the dashboard's test-fetch
/// key picker: the operator clicks a key instead of typing its path blind.
/// </summary>
internal static class CustomDataJsonKeyPathFlattener
{
    /// <summary>Caps the number of returned paths so a large/deeply-nested document can't flood the picker.</summary>
    private const int MaxPaths = 200;

    public static IReadOnlyList<string> Flatten(JToken root)
    {
        List<string> paths = [];
        Walk(root, "$", paths);
        return paths;
    }

    private static void Walk(JToken token, string path, List<string> paths)
    {
        if (paths.Count >= MaxPaths)
            return;

        switch (token)
        {
            case JObject obj:
                foreach (JProperty property in obj.Properties())
                {
                    if (paths.Count >= MaxPaths)
                        return;
                    Walk(property.Value, $"{path}.{property.Name}", paths);
                }
                break;

            case JArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (paths.Count >= MaxPaths)
                        return;
                    Walk(array[i], $"{path}[{i}]", paths);
                }
                break;

            default:
                // A leaf value (string/number/bool/null) — this is a path a field-map entry can point at.
                paths.Add(path);
                break;
        }
    }
}
