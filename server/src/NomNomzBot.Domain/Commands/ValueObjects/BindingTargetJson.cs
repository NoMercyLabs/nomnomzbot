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
