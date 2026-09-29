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
using NomNomzBot.Application.Commands.Builtin.Personality;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Reads and writes a channel's built-in overrides blob (<c>ChannelBuiltinCommand.OverridesJson</c>) —
/// <c>{ "responses": { "&lt;slot&gt;": "&lt;template&gt;" }, "speakWithTts": true }</c> (commands-pipelines.md
/// §11). One codec, so the channel registry (which applies the overrides at runtime), the built-in service (which
/// writes them) and the platform-default blast radius (which counts the channels a slot protects) can never
/// disagree about what counts as an override.
///
/// <para>
/// The pre-§11 shape <c>{ "responseTemplate": "..." }</c> is still read: it applies to the built-in's legacy
/// slots (<see cref="BuiltinResponseSlots.LegacySlotsFor"/>) unless a slot has its own entry, and
/// <see cref="Serialize"/> never writes it back — so the next write migrates it into <c>responses</c>.
/// </para>
/// </summary>
internal static class BuiltinOverridesJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The per-slot reply templates in effect for a row of <paramref name="builtinKey"/>: the explicit
    /// <c>responses</c> entries, plus a legacy <c>responseTemplate</c> filling each legacy slot that has none.
    /// Null/blank/malformed JSON reads as "no overrides", so a broken blob never crashes the registry load.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EffectiveResponses(
        string builtinKey,
        string? overridesJson
    )
    {
        BuiltinOverrides parsed = Parse(overridesJson);
        Dictionary<string, string> responses = new(
            parsed.Responses,
            StringComparer.OrdinalIgnoreCase
        );

        if (parsed.LegacyTemplate is not null)
            foreach (string slot in BuiltinResponseSlots.LegacySlotsFor(builtinKey))
                responses.TryAdd(slot, parsed.LegacyTemplate);

        return responses;
    }

    /// <summary>True when the row's blob turns "speak with TTS" on (S-OBS-12). Default off.</summary>
    public static bool SpeakWithTts(string? overridesJson) => Parse(overridesJson).SpeakWithTts;

    /// <summary>
    /// Serializes the blob, omitting an empty reply map and a false TTS flag so an unused setting never persists
    /// noise. Returns null (clear the column) when both are at their default. Never writes the legacy field.
    /// </summary>
    public static string? Serialize(
        IReadOnlyDictionary<string, string> responses,
        bool speakWithTts
    )
    {
        if (responses.Count == 0 && !speakWithTts)
            return null;

        return JsonSerializer.Serialize(
            new Payload(
                responses.Count == 0
                    ? null
                    : new SortedDictionary<string, string>(
                        responses.ToDictionary(),
                        StringComparer.Ordinal
                    ),
                speakWithTts ? true : null
            ),
            WriteOptions
        );
    }

    private static BuiltinOverrides Parse(string? overridesJson)
    {
        if (string.IsNullOrWhiteSpace(overridesJson))
            return BuiltinOverrides.None;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(overridesJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return BuiltinOverrides.None;

            Dictionary<string, string> responses = new(StringComparer.OrdinalIgnoreCase);
            if (
                root.TryGetProperty("responses", out JsonElement map)
                && map.ValueKind == JsonValueKind.Object
            )
            {
                foreach (JsonProperty entry in map.EnumerateObject())
                {
                    string? template =
                        entry.Value.ValueKind == JsonValueKind.String
                            ? entry.Value.GetString()
                            : null;
                    if (!string.IsNullOrWhiteSpace(template))
                        responses[entry.Name] = template;
                }
            }

            string? legacy =
                root.TryGetProperty("responseTemplate", out JsonElement legacyValue)
                && legacyValue.ValueKind == JsonValueKind.String
                    ? legacyValue.GetString()
                    : null;

            bool speakWithTts =
                root.TryGetProperty("speakWithTts", out JsonElement ttsValue)
                && ttsValue.ValueKind == JsonValueKind.True;

            return new BuiltinOverrides(
                responses,
                string.IsNullOrWhiteSpace(legacy) ? null : legacy,
                speakWithTts
            );
        }
        catch (JsonException)
        {
            // Malformed override JSON — ignore; the built-in keeps its default wording and stays chat-only.
            return BuiltinOverrides.None;
        }
    }

    private sealed record BuiltinOverrides(
        IReadOnlyDictionary<string, string> Responses,
        string? LegacyTemplate,
        bool SpeakWithTts
    )
    {
        public static readonly BuiltinOverrides None = new(
            new Dictionary<string, string>(),
            null,
            false
        );
    }

    private sealed record Payload(SortedDictionary<string, string>? Responses, bool? SpeakWithTts);
}
