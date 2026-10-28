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

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>The one JSON shape for a <see cref="BindingTarget"/>: camelCase names, enum kind as <c>command|pipeline|action</c>.</summary>
public static class BindingTargetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string SerializeList(IReadOnlyList<BindingTarget> targets) =>
        JsonSerializer.Serialize(targets, Options);

    /// <summary>Reads a stored list; null, empty or blank text is an empty list.</summary>
    public static IReadOnlyList<BindingTarget> DeserializeList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<BindingTarget>>(json, Options) ?? [];
}
