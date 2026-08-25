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
/// Twitch writes an EMPTY STRING, not <c>null</c>, for an absent timestamp. The clearest case is a
/// PERMANENT ban from Get Banned Users, which comes back as <c>"expires_at": ""</c> — and
/// <see cref="DateTimeOffset"/>? cannot parse <c>""</c>, so System.Text.Json threw
/// <c>The JSON value is not in a supported DateTimeOffset format</c> and the WHOLE response failed to
/// deserialize. Observed live on 2026-08-25: every banned-user import failed, so the ban registry stayed
/// empty on any channel holding a permanent ban (which is nearly all of them).
///
/// <para>This sits on the shared Helix wire options rather than on one DTO property, because the empty
/// string is a Twitch-wide convention for "no value" — the same shape appears on other optional
/// timestamps, and fixing it per-property would leave the siblings to fail one at a time.</para>
/// </summary>
public sealed class HelixEmptyStringDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            string? raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Round-trip the non-empty case through the framework parser so every format Twitch emits
            // keeps working exactly as before — this converter only adds the empty-string case.
            if (
                DateTimeOffset.TryParse(
                    raw,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed
                )
            )
                return parsed;

            throw new JsonException(
                $"Expected an ISO-8601 timestamp or an empty string, got '{raw}'."
            );
        }

        throw new JsonException(
            $"Expected a string or null for a nullable timestamp, got {reader.TokenType}."
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset? value,
        JsonSerializerOptions options
    )
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }
}
