// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.CustomEvents.EventHandlers;

/// <summary>
/// Rebuilds the nested JSON an inbound webhook carried from the dotted-key bag the journal stores
/// (<c>a.b</c> becomes <c>{"a":{"b":..}}</c>; a node whose keys are all numbers becomes an array), so a
/// custom-data field map can address it with JSONPath as it would a polled body.
/// </summary>
internal static class FlatPayloadUnflattener
{
    public static string Unflatten(string flatJson)
    {
        Dictionary<string, string>? flat = JsonConvert.DeserializeObject<
            Dictionary<string, string>
        >(flatJson);
        JObject root = new();
        if (flat is null)
            return root.ToString(Formatting.None);

        foreach (KeyValuePair<string, string> pair in flat)
        {
            string[] parts = pair.Key.Split('.');
            JObject node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (node[parts[i]] is not JObject child)
                {
                    child = new();
                    node[parts[i]] = child;
                }
                node = child;
            }
            node[parts[^1]] = pair.Value;
        }

        return ArrayifyNumericNodes(root).ToString(Formatting.None);
    }

    private static JToken ArrayifyNumericNodes(JToken token)
    {
        if (token is not JObject obj)
            return token;

        List<KeyValuePair<string, JToken?>> children =
        [
            .. obj.Properties().Select(p => new KeyValuePair<string, JToken?>(p.Name, p.Value)),
        ];
        Dictionary<string, JToken> converted = [];
        foreach (KeyValuePair<string, JToken?> child in children)
            converted[child.Key] = ArrayifyNumericNodes(child.Value!);

        if (converted.Count > 0 && converted.Keys.All(k => int.TryParse(k, out _)))
        {
            JArray array = new();
            foreach (KeyValuePair<string, JToken> item in converted.OrderBy(c => int.Parse(c.Key)))
                array.Add(item.Value);
            return array;
        }

        JObject rebuilt = new();
        foreach (KeyValuePair<string, JToken> item in converted)
            rebuilt[item.Key] = item.Value;
        return rebuilt;
    }
}
