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
using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Abstractions.Pipeline;

public sealed class ConditionDefinition
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
}
