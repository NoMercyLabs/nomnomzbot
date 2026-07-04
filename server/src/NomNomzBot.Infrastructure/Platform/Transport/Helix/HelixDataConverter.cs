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

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Reads the Helix <c>data</c> node whether Twitch sends an array (most endpoints) or a single nested
/// object (Get Channel Stream Schedule, Get/Update User Active Extensions). The object form wraps into a
/// one-element list, so every read path shares the one <see cref="HelixEnvelope{T}"/> and the sub-clients
/// stay on <c>GetSingleAsync</c> regardless of which shape the endpoint uses on the wire. Applied per
/// property on the envelope, so it never leaks into general list handling.
/// </summary>
internal sealed class HelixDataConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(ListOrSingleConverter<>).MakeGenericType(itemType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class ListOrSingleConverter<TItem> : JsonConverter<List<TItem>>
    {
        public override List<TItem>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.StartObject:
                    TItem? single = JsonSerializer.Deserialize<TItem>(ref reader, options);
                    return single is null ? [] : [single];
                case JsonTokenType.StartArray:
                    // This converter is attached per property, never registered on the options, so the
                    // plain list deserialization below uses the default converter — no re-entry.
                    return JsonSerializer.Deserialize<List<TItem>>(ref reader, options);
                default:
                    throw new JsonException(
                        $"Helix 'data' must be an array or an object, not {reader.TokenType}."
                    );
            }
        }

        public override void Write(
            Utf8JsonWriter writer,
            List<TItem> value,
            JsonSerializerOptions options
        ) => JsonSerializer.Serialize(writer, (IEnumerable<TItem>)value, options);
    }
}
