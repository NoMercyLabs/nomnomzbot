// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NomNomzBot.Application.DevPlatform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Writes a real domain event instance as the JSON a script or widget receives: the same exposed properties, the
/// same camelCase names and the same value shapes the payload schema (<see cref="JsonSchemaWriter"/>) declares.
/// It walks the event with the exact <see cref="SdkReflection"/> rules the schema and
/// <see cref="ReflectionSampleGenerator"/> use, so a sample written here cannot name a key the schema lacks.
/// </summary>
internal static class DomainEventSampleWriter
{
    public static string Write(object domainEvent, SdkContext context)
    {
        JsonObject sample = WriteObject(domainEvent, domainEvent.GetType(), context);
        return sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject WriteObject(object value, Type type, SdkContext context)
    {
        JsonObject written = new();
        foreach (PropertyInfo property in SdkReflection.ExposedProperties(type, context))
        {
            written[SdkReflection.JsonName(property)] = WriteValue(
                property.GetValue(value),
                property.PropertyType,
                context
            );
        }
        return written;
    }

    private static JsonNode? WriteValue(object? value, Type declaredType, SdkContext context)
    {
        if (value is null)
            return null;

        SdkReflection.ClassifiedType classified = SdkReflection.Classify(declaredType);
        return classified.Category switch
        {
            SdkReflection.TypeCategory.Enum => JsonValue.Create(value.ToString()),
            SdkReflection.TypeCategory.StringLike when classified.Underlying.Name == "Ulid" =>
                JsonValue.Create(value.ToString()),
            SdkReflection.TypeCategory.StringLike
            or SdkReflection.TypeCategory.IntegerLike
            or SdkReflection.TypeCategory.NumberLike
            or SdkReflection.TypeCategory.BoolLike => JsonSerializer.SerializeToNode(
                value,
                value.GetType()
            ),
            SdkReflection.TypeCategory.Collection => WriteCollection(
                (IEnumerable)value,
                classified.ElementType!,
                context
            ),
            SdkReflection.TypeCategory.Dictionary => WriteDictionary(
                (IDictionary)value,
                classified.DictValueType!,
                context
            ),
            SdkReflection.TypeCategory.Object => WriteObject(value, classified.Underlying, context),
            _ => null,
        };
    }

    private static JsonArray WriteCollection(
        IEnumerable items,
        Type elementType,
        SdkContext context
    )
    {
        JsonArray array = [];
        foreach (object? item in items)
            array.Add(WriteValue(item, elementType, context));
        return array;
    }

    private static JsonObject WriteDictionary(
        IDictionary entries,
        Type valueType,
        SdkContext context
    )
    {
        JsonObject written = new();
        foreach (DictionaryEntry entry in entries)
            written[entry.Key.ToString() ?? string.Empty] = WriteValue(
                entry.Value,
                valueType,
                context
            );
        return written;
    }
}
