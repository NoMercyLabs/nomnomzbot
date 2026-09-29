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
/// <c>{ "responses": { "&lt;slot&gt;": "&lt;template&gt;" }, "speakWithTts": true, "cooldownSeconds": 30,
/// "minPermissionLevel": 2 }</c> (commands-pipelines.md §4.5 + §11). One codec, so the channel registry (which
/// applies the overrides at runtime), the built-in services (which write them) and the platform-default blast
/// radius (which counts the channels a slot protects) can never disagree about what counts as an override.
///
/// <para>
/// Every writer goes read → <c>with { … }</c> → <see cref="Serialize"/> on the whole <see cref="BuiltinOverrides"/>
/// record, so changing one field can never silently drop another (a reply edit keeps the cooldown, a cooldown
/// edit keeps the replies).
/// </para>
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
    /// Every override a row of <paramref name="builtinKey"/> carries. The responses are the explicit
    /// <c>responses</c> entries plus a legacy <c>responseTemplate</c> filling each legacy slot that has none.
    /// Null/blank/malformed JSON reads as "no overrides", so a broken blob never crashes the registry load.
    /// </summary>
    public static BuiltinOverrides Read(string builtinKey, string? overridesJson)
    {
        ParsedBlob parsed = Parse(overridesJson);
        Dictionary<string, string> responses = new(
            parsed.Responses,
            StringComparer.OrdinalIgnoreCase
        );

        if (parsed.LegacyTemplate is not null)
            foreach (string slot in BuiltinResponseSlots.LegacySlotsFor(builtinKey))
                responses.TryAdd(slot, parsed.LegacyTemplate);

        return new BuiltinOverrides(
            responses,
            parsed.SpeakWithTts,
            parsed.CooldownSeconds,
            parsed.MinPermissionLevel
        );
    }

    /// <summary>The per-slot reply templates in effect for a row — <see cref="Read"/>'s responses.</summary>
    public static IReadOnlyDictionary<string, string> EffectiveResponses(
        string builtinKey,
        string? overridesJson
    ) => Read(builtinKey, overridesJson).Responses;

    /// <summary>
    /// Serializes the blob, omitting every field at its default (no replies, TTS off, no cooldown or floor
    /// override) so an unused setting never persists noise. Returns null (clear the column) when all of them
    /// are at their default. Never writes the legacy field.
    /// </summary>
    public static string? Serialize(BuiltinOverrides overrides)
    {
        if (overrides.IsEmpty)
            return null;

        return JsonSerializer.Serialize(
            new Payload(
                overrides.Responses.Count == 0
                    ? null
                    : new SortedDictionary<string, string>(
                        overrides.Responses.ToDictionary(),
                        StringComparer.Ordinal
                    ),
                overrides.SpeakWithTts ? true : null,
                overrides.CooldownSeconds,
                overrides.MinPermissionLevel
            ),
            WriteOptions
        );
    }

    private static ParsedBlob Parse(string? overridesJson)
    {
        if (string.IsNullOrWhiteSpace(overridesJson))
            return ParsedBlob.None;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(overridesJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ParsedBlob.None;

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

            return new ParsedBlob(
                responses,
                string.IsNullOrWhiteSpace(legacy) ? null : legacy,
                speakWithTts,
                ReadNonNegativeInt(root, "cooldownSeconds"),
                ReadNonNegativeInt(root, "minPermissionLevel")
            );
        }
        catch (JsonException)
        {
            // Malformed override JSON — ignore; the built-in keeps its defaults and stays chat-only.
            return ParsedBlob.None;
        }
    }

    // A missing, non-numeric or negative value reads as "no override" — a hand-edited blob can never turn a
    // cooldown negative or a floor below the ladder's bottom rung.
    private static int? ReadNonNegativeInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number)
        && number >= 0
            ? number
            : null;

    private sealed record ParsedBlob(
        IReadOnlyDictionary<string, string> Responses,
        string? LegacyTemplate,
        bool SpeakWithTts,
        int? CooldownSeconds,
        int? MinPermissionLevel
    )
    {
        public static readonly ParsedBlob None = new(
            new Dictionary<string, string>(),
            null,
            false,
            null,
            null
        );
    }

    private sealed record Payload(
        SortedDictionary<string, string>? Responses,
        bool? SpeakWithTts,
        int? CooldownSeconds,
        int? MinPermissionLevel
    );
}

/// <summary>
/// One channel's overrides for one built-in row. Every field at its default means "inherit the built-in's
/// behaviour": no reply texts, chat-only, the catalogue cooldown and the catalogue permission floor.
/// </summary>
/// <param name="Responses">Per-slot reply templates (commands-pipelines.md §11).</param>
/// <param name="SpeakWithTts">Also queue the reply through TTS (S-OBS-12). Default off.</param>
/// <param name="CooldownSeconds">The channel's cooldown for this built-in; null inherits the catalogue's.</param>
/// <param name="MinPermissionLevel">The channel's permission floor (unified-ladder value); null inherits.</param>
internal sealed record BuiltinOverrides(
    IReadOnlyDictionary<string, string> Responses,
    bool SpeakWithTts,
    int? CooldownSeconds,
    int? MinPermissionLevel
)
{
    /// <summary>True when nothing is overridden — the row's column is stored as null.</summary>
    public bool IsEmpty =>
        Responses.Count == 0
        && !SpeakWithTts
        && CooldownSeconds is null
        && MinPermissionLevel is null;
}
