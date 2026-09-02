// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// Validates a custom data source's field-map at SAVE time (S100 — field-map parsing). Each mapped value is a
/// JSONPath expression (Newtonsoft <c>JToken.SelectToken</c> syntax, e.g. <c>$.data.heartRate</c>) that
/// <see cref="CustomDataIngestService"/> later evaluates against the raw poll/push payload. A path with malformed
/// syntax throws at evaluation time on every future ingest if it is allowed to persist, so it is rejected here
/// instead — the same parser the poller uses, run eagerly against an empty document so only syntax is judged (no
/// live fetch is made at save time; a path that is syntactically valid but never matches anything is instead
/// caught per-ingest by <see cref="CustomDataIngestService"/> and recorded on <c>LastFieldErrorsJson</c>).
/// </summary>
internal static class CustomDataFieldMapValidator
{
    private static readonly JObject ProbeDocument = new();

    /// <summary>
    /// Returns the per-field error messages for every mapping whose JSONPath expression fails to parse. An empty
    /// result means every mapped path is syntactically valid.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(
        IReadOnlyDictionary<string, string> fieldMap
    )
    {
        Dictionary<string, string> errors = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> mapping in fieldMap)
        {
            if (string.IsNullOrWhiteSpace(mapping.Value))
            {
                errors[mapping.Key] = "The JSON path must not be empty.";
                continue;
            }

            try
            {
                // Evaluated against an empty probe document — this exercises the JSONPath parser without
                // requiring a match, so only malformed syntax is flagged here.
                ProbeDocument.SelectToken(mapping.Value);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                errors[mapping.Key] = $"Malformed JSON path '{mapping.Value}': {ex.Message}";
            }
        }

        return errors;
    }

    /// <summary>Renders a per-field error map as one human-readable message for a <c>Result</c> failure.</summary>
    public static string ToErrorMessage(IReadOnlyDictionary<string, string> errors) =>
        string.Join("; ", errors.Select(kv => $"{kv.Key}: {kv.Value}"));
}
