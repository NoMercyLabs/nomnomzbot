// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Converters;

/// <summary>
/// The hand-rolled <c>[VC:JSON]</c> column convention (platform-conventions, twitch-eventsub §1): a typed
/// property is stored as a JSON <c>string</c> column via a Newtonsoft <see cref="ValueConverter{TModel,String}"/>
/// plus a structural <see cref="ValueComparer{T}"/> (so EF change-tracking sees mutations through the string).
/// No <c>jsonb</c>, no <c>HasDefaultValueSql</c> — the converter owns the serialization and the comparer owns
/// snapshot/equality, keeping the mapping provider-agnostic (Postgres + SQLite both store a plain string).
/// </summary>
public static class JsonValueConverter
{
    // PlainObjectConverter materializes untyped `object` slots (e.g. the values of a Dictionary<string, object>
    // widget-settings column) as plain CLR — nested Dictionary<string, object?> / List<object?> / primitives —
    // instead of Newtonsoft JObject/JArray/JValue. Without it, a JSON array value round-trips to a JArray of
    // JValue; when that graph is later serialized by System.Text.Json (the API response, the overlay
    // window.WIDGET_SETTINGS injection), a JValue enumerates as empty and every array collapses to `[]` (so a
    // ["follow", …] events list reads back as [[], …] and the widget's filter silently drops every event). Plain
    // CLR round-trips cleanly through both serializers.
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new PlainObjectConverter() },
    };

    /// <summary>The converter for <typeparamref name="T"/> ⇄ a JSON <c>string</c> column.</summary>
    public static ValueConverter<T, string> Converter<T>()
        where T : class, new() =>
        new(
            model => JsonConvert.SerializeObject(model, Settings),
            column =>
                string.IsNullOrEmpty(column)
                    ? new T()
                    : JsonConvert.DeserializeObject<T>(column, Settings) ?? new T()
        );

    /// <summary>
    /// A structural comparer for <typeparamref name="T"/> that round-trips through JSON, so EF snapshots and
    /// compares by value (an in-place mutation of a collection/dictionary is detected as a change).
    /// </summary>
    public static ValueComparer<T> Comparer<T>()
        where T : class, new() =>
        new(
            (left, right) =>
                JsonConvert.SerializeObject(left, Settings)
                == JsonConvert.SerializeObject(right, Settings),
            value => JsonConvert.SerializeObject(value, Settings).GetHashCode(),
            value =>
                JsonConvert.DeserializeObject<T>(
                    JsonConvert.SerializeObject(value, Settings),
                    Settings
                ) ?? new T()
        );

    /// <summary>
    /// Reads untyped <c>object</c> slots as plain CLR (<see cref="Dictionary{TKey,TValue}"/> of string→object?,
    /// <see cref="List{T}"/> of object?, and boxed primitives) rather than Newtonsoft JTokens, so a value that
    /// later flows through System.Text.Json (API responses, the overlay settings injection) serializes as itself.
    /// Read-only: writing a plain CLR graph uses Newtonsoft's default serialization, so <see cref="CanWrite"/> is
    /// false. Only engages for <c>typeof(object)</c>, so typed columns (<c>List&lt;string&gt;</c>,
    /// <c>Dictionary&lt;string,string&gt;</c>) are untouched.
    /// </summary>
    private sealed class PlainObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(object);

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer
        ) => ReadValue(reader);

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer
        ) => throw new NotSupportedException("PlainObjectConverter is read-only.");

        private static object? ReadValue(JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    Dictionary<string, object?> map = new(StringComparer.Ordinal);
                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        string name = (string)reader.Value!;
                        reader.Read();
                        map[name] = ReadValue(reader);
                    }
                    return map;
                case JsonToken.StartArray:
                    List<object?> list = [];
                    while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                        list.Add(ReadValue(reader));
                    return list;
                case JsonToken.Integer:
                    // Newtonsoft boxes as long (or BigInteger when it overflows) — normalize to a plain long.
                    return reader.Value is long l
                        ? l
                        : Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.Float:
                    return reader.Value is double d
                        ? d
                        : Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.Boolean:
                case JsonToken.String:
                case JsonToken.Date:
                case JsonToken.Bytes:
                    return reader.Value;
                default:
                    return null;
            }
        }
    }
}
