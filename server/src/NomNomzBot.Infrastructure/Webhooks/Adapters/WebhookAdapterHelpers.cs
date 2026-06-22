// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.Webhooks.Adapters;

/// <summary>Shared parsing for the inbound adapters: case-insensitive header lookup, form decode, and JSON flattening.</summary>
internal static class WebhookAdapterHelpers
{
    /// <summary>Case-insensitive header lookup (HTTP headers are case-insensitive regardless of the dispatcher's dict).</summary>
    public static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach (KeyValuePair<string, string> header in headers)
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                return header.Value;
        return null;
    }

    /// <summary>Decodes an <c>application/x-www-form-urlencoded</c> body (Ko-fi sends a single <c>data</c> field).</summary>
    public static Dictionary<string, string> ParseForm(ReadOnlySpan<byte> body)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (
            string pair in Encoding
                .UTF8.GetString(body)
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            int equals = pair.IndexOf('=');
            if (equals < 0)
            {
                result[Uri.UnescapeDataString(pair.Replace('+', ' '))] = string.Empty;
                continue;
            }
            string key = Uri.UnescapeDataString(pair[..equals].Replace('+', ' '));
            result[key] = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
        }
        return result;
    }

    /// <summary>Flattens a JSON document into a dotted-key string→string bag; malformed JSON yields an empty bag.</summary>
    public static Dictionary<string, string> FlattenJson(string json)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        try
        {
            Flatten(JToken.Parse(json), string.Empty, variables);
        }
        catch (JsonException)
        {
            // Malformed JSON — callers treat an empty bag / missing fields as a verification or parse failure.
        }
        return variables;
    }

    private static void Flatten(JToken token, string prefix, Dictionary<string, string> variables)
    {
        switch (token)
        {
            case JObject obj:
                foreach (JProperty property in obj.Properties())
                    Flatten(
                        property.Value,
                        prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}",
                        variables
                    );
                break;
            case JArray array:
                for (int i = 0; i < array.Count; i++)
                    Flatten(array[i], $"{prefix}.{i}", variables);
                break;
            default:
                if (prefix.Length != 0)
                    variables[prefix] = token.ToString();
                break;
        }
    }
}
