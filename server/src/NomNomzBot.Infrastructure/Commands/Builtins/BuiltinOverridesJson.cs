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

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Reads a channel's built-in override (<c>ChannelBuiltinCommand.OverridesJson</c>). One reader, so the channel
/// registry (which applies the override at runtime) and the platform-default blast radius (which counts the
/// channels it protects) can never disagree about what counts as an override.
/// </summary>
internal static class BuiltinOverridesJson
{
    /// <summary>
    /// Extracts the response-template override — schema <c>{ "responseTemplate": "..." }</c>. Returns false for
    /// null/blank/malformed JSON or an empty template, so a broken override never crashes the registry load and
    /// simply leaves the built-in on its default wording.
    /// </summary>
    public static bool TryGetResponseTemplate(string? overridesJson, out string template)
    {
        template = string.Empty;
        if (string.IsNullOrWhiteSpace(overridesJson))
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(overridesJson);
            if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("responseTemplate", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
            )
            {
                string? parsed = value.GetString();
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    template = parsed;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed override JSON — ignore; the built-in keeps its default wording.
        }

        return false;
    }
}
