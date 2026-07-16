// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NomNomzBot.Infrastructure.Supporters.Adapters;

/// <summary>
/// Shared parsing for the supporter sources (supporter-events.md §0 D2): the flat dotted-key view of a
/// journaled webhook body, plus the common money/text normalizations. Keys are case-insensitive (provider
/// payload casing varies); scalar values render culture-invariantly so a JSON number like <c>2.5</c> never
/// becomes <c>"2,5"</c> under a comma-decimal locale and mis-parses as the amount.
/// </summary>
internal static class SupporterAdapterHelpers
{
    /// <summary>
    /// Reads the payload as a flat dotted-key bag. The journaled body is already a flat object; a nested body
    /// (direct test feed) is flattened here so both shapes normalize to the same keys. Unparseable JSON yields
    /// an empty bag so the caller fails loudly rather than persisting junk.
    /// </summary>
    public static Dictionary<string, string> ReadFlatFields(string rawPayload)
    {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            Flatten(JToken.Parse(rawPayload), string.Empty, fields);
        }
        catch (JsonException)
        {
            // Unparseable — an empty bag makes the caller fail loudly rather than persist junk.
        }
        return fields;
    }

    private static void Flatten(JToken token, string prefix, Dictionary<string, string> fields)
    {
        switch (token)
        {
            case JObject obj:
                foreach (JProperty property in obj.Properties())
                    Flatten(
                        property.Value,
                        prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}",
                        fields
                    );
                break;
            case JArray array:
                for (int i = 0; i < array.Count; i++)
                    Flatten(array[i], $"{prefix}.{i}", fields);
                break;
            default:
                if (prefix.Length != 0)
                    // Invariant scalar text: JToken.ToString() would render a JSON number with the current
                    // culture (e.g. "2,5" under a comma-decimal locale), which then mis-parses as the amount.
                    fields[prefix] = token is JValue { Value: IFormattable formattable }
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : token.ToString();
                break;
        }
    }

    /// <summary>
    /// Parses a major-unit amount ("5", "5.00") into minor units (500) with its ISO currency code. Providers
    /// that already send minor units (Patreon cents) must NOT go through this.
    /// </summary>
    public static (long? AmountMinor, string? Currency) ParseMajorAmount(
        string? amount,
        string? currency
    )
    {
        if (
            string.IsNullOrWhiteSpace(amount)
            || !decimal.TryParse(
                amount,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value
            )
        )
            return (null, null);

        long minor = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
        return (minor, NormalizeCurrency(currency));
    }

    /// <summary>Uppercased 3-letter ISO code, or null when the provider sent none.</summary>
    public static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return null;
        string code = currency.Trim().ToUpperInvariant();
        return code.Length > 3 ? code[..3] : code;
    }

    /// <summary>Whitespace-trimmed and length-capped, with a fallback for blank input.</summary>
    public static string Trimmed(string? value, string fallback, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>Counts the distinct <c>N</c> indices under an array prefix like <c>line_items.</c> in the flat bag.</summary>
    public static int CountArrayItems(Dictionary<string, string> fields, string arrayPrefix)
    {
        HashSet<string> indices = new(StringComparer.Ordinal);
        foreach (string key in fields.Keys)
        {
            if (!key.StartsWith(arrayPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = key[arrayPrefix.Length..];
            int dot = rest.IndexOf('.');
            indices.Add(dot < 0 ? rest : rest[..dot]);
        }
        return indices.Count;
    }
}
