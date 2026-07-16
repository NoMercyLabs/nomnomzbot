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
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;

namespace NomNomzBot.Infrastructure.Supporters.Adapters;

/// <summary>
/// Fourthwall monetization adapter (supporter-events.md §0 D2). Normalizes a Fourthwall <c>DONATION</c> webhook
/// into a <see cref="SupporterEventDraft"/> of kind <c>tip</c>. Fourthwall's amount lives at
/// <c>data.amounts.total.{value,currency}</c> (major units), the supporter name at <c>data.username</c>, the
/// note at <c>data.message</c>, and the dedupe id at <c>data.id</c>. The inbound plane journals a dotted
/// key→string bag, so this reads the flat keys. Non-donation events (merch <c>ORDER_PLACED</c>, …) are declined
/// — their payload shapes are not yet modeled, so <see cref="Capabilities"/> advertises only <c>tip</c>.
/// </summary>
public sealed class FourthwallSupporterSource : ISupporterSource
{
    public string SourceKey => "fourthwall";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip"], ConnectionMode: "webhook", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed Fourthwall payload.",
                    "VALIDATION_FAILED"
                )
            );

        string type = fields.GetValueOrDefault("type", string.Empty).ToLowerInvariant();
        if (type != "donation")
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    $"Unsupported Fourthwall event '{type}'.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = ParseAmount(
            fields.GetValueOrDefault("data.amounts.total.value"),
            fields.GetValueOrDefault("data.amounts.total.currency")
        );

        string transactionId =
            fields.GetValueOrDefault("data.id")
            ?? fields.GetValueOrDefault("webhookId")
            ?? fields.GetValueOrDefault("id")
            ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: Trimmed(
                fields.GetValueOrDefault("data.username"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: null,
            Quantity: null,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("data.message"))
                ? null
                : fields.GetValueOrDefault("data.message"),
            IsRecurring: false,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>
    /// Reads the payload as a flat dotted-key bag. The journaled body is already a flat object; a nested body
    /// (direct test feed) is flattened here so both shapes normalize to the same <c>data.*</c> keys.
    /// </summary>
    private static Dictionary<string, string> ReadFlatFields(string rawPayload)
    {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            Flatten(JToken.Parse(rawPayload), string.Empty, fields);
        }
        catch (Newtonsoft.Json.JsonException)
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

    /// <summary>Fourthwall sends the amount in the currency's major units (10 = $10.00); we store minor units.</summary>
    private static (long?, string?) ParseAmount(string? amount, string? currency)
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
        string? code = string.IsNullOrWhiteSpace(currency)
            ? null
            : currency.Trim().ToUpperInvariant();
        if (code is { Length: > 3 })
            code = code[..3];
        return (minor, code);
    }

    /// <summary>A stable dedup id when Fourthwall omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("type", ""),
            fields.GetValueOrDefault("data.username", ""),
            fields.GetValueOrDefault("data.amounts.total.value", ""),
            fields.GetValueOrDefault("data.createdAt", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "fourthwall-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string Trimmed(string? value, string fallback, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
