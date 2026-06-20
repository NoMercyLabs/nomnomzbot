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

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Null-tolerant readers over a raw EventSub <see cref="JsonElement"/> event payload. Twitch omits or nulls
/// fields situationally (an anonymous cheer has no <c>user_id</c>; an optional sub message has no <c>emotes</c>),
/// so every reader degrades to a safe default instead of throwing — a translator stays on the hot path and never
/// faults the dispatcher over a missing field. Shared by every translator so payload parsing reads identically
/// across the whole fan-out.
/// </summary>
internal static class EventSubPayload
{
    /// <summary>The string value of <paramref name="name"/>, or <c>null</c> when absent / not a string.</summary>
    public static string? GetString(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The string value of <paramref name="name"/>, or empty string when absent (for required fields).</summary>
    public static string GetRequiredString(this JsonElement element, string name) =>
        element.GetString(name) ?? string.Empty;

    /// <summary>The integer value of <paramref name="name"/>, or <c>0</c> when absent / not a number.</summary>
    public static int GetInt(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : 0;

    /// <summary>The long value of <paramref name="name"/>, or <c>0</c> when absent / not a number.</summary>
    public static long GetLong(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long parsed)
            ? parsed
            : 0;

    /// <summary>The boolean value of <paramref name="name"/>, or <c>false</c> when absent / not a boolean.</summary>
    public static bool GetBool(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    /// <summary>The timestamp value of <paramref name="name"/>, or <c>null</c> when absent / unparseable.</summary>
    public static DateTimeOffset? GetDateTimeOffset(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out DateTimeOffset parsed)
            ? parsed
            : null;

    /// <summary>The nested object at <paramref name="name"/>, or <c>null</c> when absent / not an object.</summary>
    public static JsonElement? GetObject(this JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
}
