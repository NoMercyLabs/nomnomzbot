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
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Turns the JSON a script hands <c>nnz.api.tts.speakSequence</c> into the ordered <see cref="TtsSpeakSegment"/>
/// list one dispatch carries. Pure: a bad shape is a message, never an exception and never a partial list.
/// </summary>
internal static class ScriptTtsSegmentParser
{
    /// <summary>The most parts one utterance may carry.</summary>
    internal const int MaxSegments = 20;

    /// <summary>The longest silence a part may ask for after itself, in milliseconds.</summary>
    internal const int MaxBreakMs = 10_000;

    /// <summary>Parses <paramref name="json"/>; on failure <paramref name="error"/> says what is wrong.</summary>
    internal static List<TtsSpeakSegment>? TryParse(string? json, out string error)
    {
        error = "tts.speakSequence needs an array of { text } segments.";
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;
            if (root.GetArrayLength() > MaxSegments)
            {
                error = $"tts.speakSequence takes at most {MaxSegments} segments.";
                return null;
            }

            List<TtsSpeakSegment> segments = [];
            int index = 0;
            foreach (JsonElement item in root.EnumerateArray())
            {
                TtsSpeakSegment? segment = ParseItem(item, index, out string itemError);
                if (segment is null)
                {
                    error = itemError;
                    return null;
                }
                segments.Add(segment);
                index++;
            }
            return segments;
        }
    }

    private static TtsSpeakSegment? ParseItem(JsonElement item, int index, out string error)
    {
        error = $"tts.speakSequence segment {index} needs text.";
        if (
            item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("text", out JsonElement text)
            || text.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(text.GetString())
        )
            return null;

        string? voice = null;
        if (item.TryGetProperty("voice", out JsonElement voiceElement))
        {
            if (voiceElement.ValueKind == JsonValueKind.String)
                voice = string.IsNullOrWhiteSpace(voiceElement.GetString())
                    ? null
                    : voiceElement.GetString();
            else if (voiceElement.ValueKind != JsonValueKind.Null)
            {
                error = $"tts.speakSequence segment {index} voice must be a string.";
                return null;
            }
        }

        if (
            !TryNumber(item, "rate", out double? rate)
            || !TryNumber(item, "pitch", out double? pitch)
            || !TryNumber(item, "breakAfterMs", out double? breakMs)
        )
        {
            error =
                $"tts.speakSequence segment {index} rate, pitch and breakAfterMs must be numbers.";
            return null;
        }

        if (breakMs is < 0 or > MaxBreakMs)
        {
            error = $"tts.speakSequence segment {index} breakAfterMs must be 0 to {MaxBreakMs}.";
            return null;
        }

        return new TtsSpeakSegment(
            text.GetString()!,
            voice,
            rate,
            pitch,
            (int)Math.Round(breakMs ?? 0)
        );
    }

    // Absent or null is "no override" (true, null); a present non-number is a malformed segment (false).
    private static bool TryNumber(JsonElement item, string name, out double? value)
    {
        value = null;
        if (!item.TryGetProperty(name, out JsonElement element))
            return true;
        if (element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double number))
            return false;
        value = number;
        return true;
    }
}
