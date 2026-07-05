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

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// System.Text.Json converter that encodes every <see cref="Guid"/> as a ULID string on write and decodes a ULID
/// OR a raw Guid string on read (<see cref="GuidUlidCodec"/>). Registered on the MVC response serializer and the
/// SignalR payload serializer so REST and hub JSON speak the same owned-id form. System.Text.Json reuses this
/// converter for <c>Guid?</c> automatically (a null value stays null and never reaches the converter). The
/// property-name overrides keep <c>Dictionary&lt;Guid, T&gt;</c> keys on the same ULID encoding.
/// </summary>
public sealed class UlidGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(
                $"Expected a ULID/GUID string identifier but found {reader.TokenType}."
            );

        return Decode(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(GuidUlidCodec.Encode(value));

    public override Guid ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => Decode(reader.GetString());

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        Guid value,
        JsonSerializerOptions options
    ) => writer.WritePropertyName(GuidUlidCodec.Encode(value));

    private static Guid Decode(string? value)
    {
        if (GuidUlidCodec.TryDecode(value, out Guid id))
            return id;

        throw new JsonException($"'{value}' is not a valid ULID or GUID identifier.");
    }
}
