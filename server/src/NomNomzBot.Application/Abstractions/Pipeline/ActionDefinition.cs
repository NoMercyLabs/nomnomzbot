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
using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Abstractions.Pipeline;

public sealed class ActionDefinition
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Parameters { get; set; }

    /// <summary>Gets a string parameter value by key.</summary>
    public string? GetString(string key)
    {
        if (Parameters is null)
            return null;
        if (!Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.ToString();
    }

    /// <summary>Gets an int parameter value by key.</summary>
    public int GetInt(string key, int defaultValue = 0)
    {
        if (Parameters is null)
            return defaultValue;
        if (!Parameters.TryGetValue(key, out JsonElement elem))
            return defaultValue;
        return elem.ValueKind == JsonValueKind.Number ? elem.GetInt32() : defaultValue;
    }

    /// <summary>Gets a number parameter by key, or null when it is absent or not a number (a numeric string counts).</summary>
    public double? GetDouble(string key)
    {
        if (Parameters is null || !Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.String
                when double.TryParse(
                    elem.GetString(),
                    CultureInfo.InvariantCulture,
                    out double parsed
                ) => parsed,
            _ => null,
        };
    }

    /// <summary>Gets a bool parameter value by key.</summary>
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (Parameters is null)
            return defaultValue;
        if (!Parameters.TryGetValue(key, out JsonElement elem))
            return defaultValue;
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }
}
