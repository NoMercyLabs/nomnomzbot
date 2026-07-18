// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json.Nodes;
using NomNomzBot.Application.DevPlatform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Reflects one event payload type into a JSON Schema (dev-platform.md §1.3) for the event-catalog endpoint. It
/// honours the same context/exposure rules as the TypeScript writer (both read <see cref="SdkReflection"/>), so
/// the schema and the <c>.d.ts</c> describe the identical shape. Nested value objects are inlined; a reference
/// cycle stops at a shallow <c>{ "type": "object" }</c>.
/// </summary>
internal sealed class JsonSchemaWriter
{
    private readonly SdkContext _context;

    public JsonSchemaWriter(SdkContext context) => _context = context;

    public JsonObject BuildPayloadSchema(Type type) => BuildObjectSchema(type, new HashSet<Type>());

    private JsonObject BuildObjectSchema(Type type, IReadOnlySet<Type> ancestors)
    {
        HashSet<Type> branch = [.. ancestors, type];

        JsonObject properties = new();
        JsonArray required = [];
        foreach (PropertyInfo property in SdkReflection.ExposedProperties(type, _context))
        {
            string name = SdkReflection.JsonName(property);
            bool nullable = SdkReflection.IsNullable(property);
            properties[name] = SchemaFor(property.PropertyType, nullable, branch);
            if (!nullable)
                required.Add(name);
        }

        JsonObject schema = new() { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
            schema["required"] = required;
        schema["additionalProperties"] = false;
        return schema;
    }

    private JsonNode SchemaFor(Type type, bool nullable, IReadOnlySet<Type> ancestors)
    {
        SdkReflection.ClassifiedType classified = SdkReflection.Classify(type);
        JsonNode node = classified.Category switch
        {
            SdkReflection.TypeCategory.StringLike => StringSchema(classified.Underlying),
            SdkReflection.TypeCategory.IntegerLike => new JsonObject { ["type"] = "integer" },
            SdkReflection.TypeCategory.NumberLike => new JsonObject { ["type"] = "number" },
            SdkReflection.TypeCategory.BoolLike => new JsonObject { ["type"] = "boolean" },
            SdkReflection.TypeCategory.Enum => EnumSchema(classified.Underlying),
            SdkReflection.TypeCategory.Collection => new JsonObject
            {
                ["type"] = "array",
                ["items"] = SchemaFor(classified.ElementType!, false, ancestors),
            },
            SdkReflection.TypeCategory.Dictionary => new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = SchemaFor(classified.DictValueType!, false, ancestors),
            },
            SdkReflection.TypeCategory.Object => ancestors.Contains(classified.Underlying)
                ? new JsonObject { ["type"] = "object" }
                : BuildObjectSchema(classified.Underlying, ancestors),
            _ => new JsonObject(),
        };

        return nullable ? MakeNullable(node) : node;
    }

    private static JsonObject StringSchema(Type underlying)
    {
        JsonObject node = new() { ["type"] = "string" };
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            node["format"] = "date-time";
        else if (underlying == typeof(DateOnly))
            node["format"] = "date";
        else if (underlying == typeof(TimeOnly))
            node["format"] = "time";
        else if (underlying == typeof(Guid))
            node["format"] = "uuid";
        return node;
    }

    private static JsonObject EnumSchema(Type enumType)
    {
        JsonArray values = [];
        foreach (string name in Enum.GetNames(enumType))
            values.Add(name);
        return new JsonObject { ["type"] = "string", ["enum"] = values };
    }

    // A nullable property widens its "type" to include "null"; an untyped (any) schema is left untouched.
    private static JsonNode MakeNullable(JsonNode node)
    {
        if (
            node is JsonObject obj
            && obj["type"] is JsonValue value
            && value.TryGetValue(out string? typeName)
        )
        {
            obj["type"] = new JsonArray(typeName, "null");
        }
        return node;
    }
}
