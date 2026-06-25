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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

/// <summary>
/// Makes the single provider-agnostic EF model build on BOTH providers (deployment-profile design: "one model,
/// avoid provider-specific column types, provider selected at runtime"). The model is authored Postgres-first —
/// some columns carry Npgsql-native shapes (<c>jsonb</c>, <c>hstore</c>, <c>text[]</c>) or CLR collection/dictionary
/// types that only Npgsql maps natively. Under SQLite those have no portable mapping, so this step rewrites them to
/// a <c>TEXT</c> column backed by a Newtonsoft JSON value converter — the SAME JSON Npgsql stores, so the migration
/// sets differ only in the column's declared type. On Postgres this is never invoked (the native mappings stand).
/// </summary>
public static class ProviderCompatibilityExtensions
{
    // The complex collection / dictionary CLR types the model carries (Npgsql maps them natively to jsonb/hstore).
    // Under SQLite, EF's relationship-discovery convention would otherwise treat them as navigations to owned
    // entities and fail — so they are pre-claimed as scalar JSON properties in ConfigureConventions, BEFORE
    // discovery runs. Primitive collections (List<string>, string[]) are NOT here: EF 10 maps those to JSON on
    // SQLite on its own; they only need their column type reset in ApplySqliteCompatibility.
    // Dictionary<string, object> is intentionally NOT here: EF reserves it as the shared-type-entity backing type,
    // so it cannot be claimed via Properties(...). It is mapped property-locally (see ApplyScalarJsonProperties).
    private static readonly Type[] ComplexJsonPropertyTypes =
    [
        typeof(Dictionary<string, string>),
        typeof(List<Domain.Chat.ValueObjects.ChatBadge>),
        typeof(List<Domain.Chat.ValueObjects.ChatMessageFragment>),
    ];

    /// <summary>
    /// Pre-claims the model's complex collection/dictionary CLR types as scalar JSON properties (SQLite only),
    /// so EF's relationship-discovery never mistakes them for navigations. Called from
    /// <c>AppDbContext.ConfigureConventions</c> before the model is built.
    /// </summary>
    public static void ConfigureSqliteJsonConventions(
        this ModelConfigurationBuilder configurationBuilder
    )
    {
        foreach (Type type in ComplexJsonPropertyTypes)
        {
            Type converterType = typeof(JsonValueConverter<>).MakeGenericType(type);
            Type comparerType = typeof(JsonValueComparer<>).MakeGenericType(type);
            configurationBuilder.Properties(type).HaveConversion(converterType, comparerType);
        }
    }

    /// <summary>
    /// Maps every <c>Dictionary&lt;string, object&gt;</c> member as a scalar JSON property (SQLite only). This type
    /// can't be claimed by the convention (EF reserves it for shared-type entities), so each owning entity's
    /// property is configured directly — calling <c>.Property(...)</c> pre-empts relationship discovery. Must run
    /// inside <c>OnModelCreating</c> before the model is finalized.
    /// </summary>
    public static void ApplyScalarJsonProperties(this ModelBuilder modelBuilder)
    {
        Type converterType = typeof(JsonValueConverter<Dictionary<string, object>>);
        Type comparerType = typeof(JsonValueComparer<Dictionary<string, object>>);
        ValueConverter converter = (ValueConverter)Activator.CreateInstance(converterType)!;
        ValueComparer comparer = (ValueComparer)Activator.CreateInstance(comparerType)!;

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (
                System.Reflection.PropertyInfo clrProperty in entityType.ClrType.GetProperties()
            )
            {
                if (clrProperty.PropertyType != typeof(Dictionary<string, object>))
                    continue;

                modelBuilder
                    .Entity(entityType.ClrType)
                    .Property(clrProperty.Name)
                    .HasConversion(converter, comparer);
            }
        }
    }

    public static void ApplySqliteCompatibility(this ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                string? columnType = property.GetColumnType();
                Type clrType = property.ClrType;

                bool isNpgsqlNativeColumn =
                    columnType
                    is "jsonb"
                        or "json"
                        or "hstore"
                        or "text[]"
                        or "integer[]"
                        or "uuid[]";

                // A CLR collection / dictionary that Npgsql maps natively (text[]/hstore) but SQLite cannot map at
                // all. Detected structurally so no property name has to be enumerated by hand.
                bool isUnmappableClr = IsNonPrimitiveCollectionOrDictionary(clrType);

                bool hasConverter = property.GetValueConverter() is not null;

                if (!isNpgsqlNativeColumn && !isUnmappableClr && !hasConverter)
                    continue;

                // Portable storage: a TEXT column holding the same JSON. Clear the Npgsql default SQL (e.g.
                // '[]'::jsonb) which is invalid on SQLite, and the Npgsql-only value generation flag.
                property.SetColumnType("TEXT");
                property.SetDefaultValueSql(null);
                property.ValueGenerated = ValueGenerated.Never;

                // Complex types already got their JSON converter from ConfigureConventions — don't double-wrap.
                // Apply the JSON converter here only for the collection/array CLR types reached purely by column
                // type (and never for plain JSON-text strings, which need no converter).
                if (!hasConverter && clrType != typeof(string))
                {
                    property.SetValueConverter(BuildJsonConverter(clrType));
                    property.SetValueComparer(BuildJsonComparer(clrType));
                }
            }
        }
    }

    // string is a primitive even though it implements IEnumerable; a jsonb-typed string column (e.g. Record.Data)
    // is already JSON text and needs no converter — only its TEXT column type is reset above.
    private static bool IsNonPrimitiveCollectionOrDictionary(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive)
            return false;
        if (typeof(IDictionary).IsAssignableFrom(type))
            return true;
        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        // A collection of a non-primitive element (List<ChatBadge>, List<string>, string[]) — Npgsql maps it
        // (jsonb / text[]); SQLite needs the JSON converter.
        Type? element = GetElementType(type);
        return element is not null;
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (type.IsGenericType)
        {
            Type[] args = type.GetGenericArguments();
            return args.Length > 0 ? args[^1] : null;
        }
        return null;
    }

    // string columns typed as jsonb (e.g. Record.Data / PipelineJson) are already JSON text — no converter, just TEXT.
    private static ValueConverter? BuildJsonConverter(Type clrType)
    {
        if (clrType == typeof(string))
            return null;

        Type converterType = typeof(JsonValueConverter<>).MakeGenericType(clrType);
        return (ValueConverter?)Activator.CreateInstance(converterType);
    }

    private static ValueComparer? BuildJsonComparer(Type clrType)
    {
        if (clrType == typeof(string))
            return null;

        Type comparerType = typeof(JsonValueComparer<>).MakeGenericType(clrType);
        return (ValueComparer?)Activator.CreateInstance(comparerType);
    }

    /// <summary>Round-trips a value to/from JSON text (Newtonsoft, the project's [VC:JSON] serializer).</summary>
    private sealed class JsonValueConverter<T> : ValueConverter<T, string>
    {
        public JsonValueConverter()
            : base(
                v => JsonConvert.SerializeObject(v),
                v =>
                    string.IsNullOrEmpty(v)
                        ? Activator.CreateInstance<T>()
                        : JsonConvert.DeserializeObject<T>(v)!
            ) { }
    }

    /// <summary>Compares JSON-backed collection/object values by their serialized form (snapshot via re-serialize).</summary>
    private sealed class JsonValueComparer<T> : ValueComparer<T>
    {
        public JsonValueComparer()
            : base(
                (a, b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b),
                v => v == null ? 0 : JsonConvert.SerializeObject(v).GetHashCode(),
                v =>
                    v == null
                        ? default!
                        : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(v))!
            ) { }
    }
}
