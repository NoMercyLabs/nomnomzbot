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
using FluentAssertions;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// Contract guard for the committed <c>server/openapi/v1.json</c> snapshot: a free-form map
/// (<c>Dictionary&lt;string, object?&gt;</c>, <c>JsonElement</c>-typed object) must publish
/// <c>additionalProperties</c>. A bare <c>{ "type": "object" }</c> makes openapi-typescript emit
/// <c>Record&lt;string, never&gt;</c>, so the dashboard cannot write a single value into it.
/// </summary>
public sealed class OpenApiOpenMapTests
{
    [Fact]
    public void Every_object_schema_without_properties_publishes_additionalProperties()
    {
        JsonElement schemas = LoadSnapshot().GetProperty("components").GetProperty("schemas");
        List<string> bare = [];

        foreach (JsonProperty schema in schemas.EnumerateObject())
            Walk(schema.Value, "#/components/schemas/" + schema.Name, bare);

        bare.Should()
            .BeEmpty(
                "{0}",
                $"{bare.Count} object schema(s) are bare maps with no additionalProperties: {string.Join(", ", bare)}"
            );
    }

    private static void Walk(JsonElement node, string path, List<string> bare)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in node.EnumerateArray())
                Walk(item, $"{path}/{index++}", bare);
            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (IsBareObject(node))
            bare.Add(path);

        foreach (JsonProperty child in node.EnumerateObject())
            Walk(child.Value, $"{path}/{child.Name}", bare);
    }

    private static bool IsBareObject(JsonElement node)
    {
        if (!node.TryGetProperty("type", out JsonElement type))
            return false;

        bool isObject =
            type.ValueKind == JsonValueKind.String
                ? type.GetString() == "object"
                : type.ValueKind == JsonValueKind.Array
                    && type.EnumerateArray().Any(t => t.GetString() == "object");

        return isObject
            && !node.TryGetProperty("properties", out _)
            && !node.TryGetProperty("additionalProperties", out _)
            && !node.TryGetProperty("allOf", out _)
            && !node.TryGetProperty("oneOf", out _)
            && !node.TryGetProperty("anyOf", out _);
    }

    private static JsonElement LoadSnapshot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string path = Path.Combine(dir.FullName, "server", "openapi", "v1.json");
            if (!File.Exists(path))
                path = Path.Combine(dir.FullName, "openapi", "v1.json");
            if (File.Exists(path))
                return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            dir = dir.Parent;
        }

        throw new FileNotFoundException("server/openapi/v1.json not found");
    }
}
