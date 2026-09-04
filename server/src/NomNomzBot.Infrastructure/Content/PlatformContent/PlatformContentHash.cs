// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NomNomzBot.Infrastructure.Content.PlatformContent;

/// <summary>
/// Canonicalizes a JSON payload (object keys sorted, recursively, whitespace-normalized) and hashes it —
/// the mechanism <c>PlatformContentVersion.ContentHash</c> and the tenant-side <c>PlatformSourceHash</c>
/// (platform-admin.md §2.1, §3.2-§3.3) both compute, so two byte-different-but-semantically-equal JSON
/// documents (key order, whitespace) still compare equal for "untouched" detection.
/// </summary>
public static class PlatformContentHash
{
    /// <summary>SHA-256 of the canonicalized JSON, as lowercase hex. Null/empty input hashes as <c>"{}"</c>.</summary>
    public static string ComputeHash(string? json)
    {
        string canonical = Canonicalize(json);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Re-serializes <paramref name="json"/> with every object's keys sorted ordinally and no
    /// insignificant whitespace. Null/empty input canonicalizes to <c>"{}"</c> (the "no overrides" shape).</summary>
    public static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "{}";

        using JsonDocument doc = JsonDocument.Parse(json);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(doc.RootElement, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (
                    JsonProperty property in element
                        .EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                )
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
