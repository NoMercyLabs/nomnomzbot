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
using System.Text.Json;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The shared reflection layer both SDK emitters (TypeScript + JSON Schema) walk (dev-platform.md §1.3). It
/// decides, for one context, which properties are exposed (honouring <c>[NotExposed]</c>/<c>[Pii]</c> and
/// dropping the <see cref="DomainEventBase"/> transport metadata), their wire names, nullability, and the
/// category of each property type. Keeping this in one place is what makes the two generated artifacts and the
/// runtime projection agree by construction — they read the same declarations the same way.
/// </summary>
public static class SdkReflection
{
    /// <summary>How a property's (already null-unwrapped) type maps onto the SDK surface.</summary>
    public enum TypeCategory
    {
        /// <summary><c>string</c> / <c>Guid</c> / <c>Ulid</c> / date-time-like → TS <c>string</c>.</summary>
        StringLike,

        /// <summary>Integral CLR numbers → JSON <c>integer</c> (TS <c>number</c>).</summary>
        IntegerLike,

        /// <summary><c>decimal</c>/<c>double</c>/<c>float</c> → JSON/TS <c>number</c>.</summary>
        NumberLike,

        /// <summary><c>bool</c> → <c>boolean</c>.</summary>
        BoolLike,

        /// <summary>A CLR enum → string-literal union of its member names.</summary>
        Enum,

        /// <summary>An <c>IEnumerable&lt;T&gt;</c> (not string) → array of the element type.</summary>
        Collection,

        /// <summary>An <c>IDictionary&lt;,&gt;</c>/<c>IReadOnlyDictionary&lt;,&gt;</c> → keyed object of the value type.</summary>
        Dictionary,

        /// <summary>A POCO record/class/struct → its own interface / nested object schema.</summary>
        Object,

        /// <summary>Anything unresolvable (bare <c>object</c>, an interface) → TS <c>unknown</c>.</summary>
        Unknown,
    }

    /// <summary>The classification of a property type, with the element/value type for collections/dictionaries.</summary>
    public readonly record struct ClassifiedType(
        TypeCategory Category,
        Type Underlying,
        Type? ElementType,
        Type? DictValueType
    );

    /// <summary>
    /// The exposed public properties of <paramref name="type"/> for <paramref name="context"/>, in declaration
    /// order. Drops <see cref="DomainEventBase"/> transport metadata (eventId/occurredAt/broadcasterId),
    /// <c>[NotExposed]</c> properties always, and <c>[Pii]</c> properties for the widget context.
    /// </summary>
    public static IReadOnlyList<PropertyInfo> ExposedProperties(Type type, SdkContext context)
    {
        return
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => p.DeclaringType != typeof(DomainEventBase))
                .Where(p => p.GetCustomAttribute<NotExposedAttribute>() is null)
                .Where(p =>
                    context == SdkContext.Script || p.GetCustomAttribute<PiiAttribute>() is null
                )
                .OrderBy(p => p.MetadataToken),
        ];
    }

    /// <summary>The camelCase wire name of a property — the exact policy the JSON serializer uses.</summary>
    public static string JsonName(PropertyInfo property) =>
        JsonNamingPolicy.CamelCase.ConvertName(property.Name);

    /// <summary>
    /// True when a property may be null on the wire — a nullable value type (<c>T?</c>) OR a nullable reference
    /// type (<c>string?</c>), read from the compiler's nullable annotations.
    /// </summary>
    public static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true;
        if (property.PropertyType.IsValueType)
            return false;

        NullabilityInfo info = new NullabilityInfoContext().Create(property);
        return info.ReadState == NullabilityState.Nullable;
    }

    /// <summary>Classifies a property type onto the SDK surface, unwrapping <c>Nullable&lt;T&gt;</c> first.</summary>
    public static ClassifiedType Classify(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(string) || t == typeof(Guid) || t.Name == "Ulid")
            return new ClassifiedType(TypeCategory.StringLike, t, null, null);
        if (
            t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
        )
            return new ClassifiedType(TypeCategory.StringLike, t, null, null);
        if (t.IsEnum)
            return new ClassifiedType(TypeCategory.Enum, t, null, null);
        if (t == typeof(bool))
            return new ClassifiedType(TypeCategory.BoolLike, t, null, null);
        if (IsIntegral(t))
            return new ClassifiedType(TypeCategory.IntegerLike, t, null, null);
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float))
            return new ClassifiedType(TypeCategory.NumberLike, t, null, null);

        if (TryGetDictionaryValueType(t, out Type? valueType))
            return new ClassifiedType(TypeCategory.Dictionary, t, null, valueType);
        if (TryGetEnumerableElementType(t, out Type? elementType))
            return new ClassifiedType(TypeCategory.Collection, t, elementType, null);

        if (t.IsClass || (t.IsValueType && !t.IsPrimitive))
            return new ClassifiedType(TypeCategory.Object, t, null, null);

        return new ClassifiedType(TypeCategory.Unknown, t, null, null);
    }

    private static bool IsIntegral(Type t) =>
        t == typeof(byte)
        || t == typeof(sbyte)
        || t == typeof(short)
        || t == typeof(ushort)
        || t == typeof(int)
        || t == typeof(uint)
        || t == typeof(long)
        || t == typeof(ulong)
        || t == typeof(nint)
        || t == typeof(nuint);

    private static bool TryGetDictionaryValueType(Type t, out Type? valueType)
    {
        foreach (Type i in Interfaces(t))
        {
            if (!i.IsGenericType)
                continue;
            Type def = i.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                valueType = i.GetGenericArguments()[1];
                return true;
            }
        }
        valueType = null;
        return false;
    }

    private static bool TryGetEnumerableElementType(Type t, out Type? elementType)
    {
        if (t.IsArray)
        {
            elementType = t.GetElementType();
            return elementType is not null;
        }

        foreach (Type i in Interfaces(t))
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = i.GetGenericArguments()[0];
                return true;
            }
        }
        elementType = null;
        return false;
    }

    private static IEnumerable<Type> Interfaces(Type t) =>
        t.IsInterface ? [t, .. t.GetInterfaces()] : t.GetInterfaces();
}
