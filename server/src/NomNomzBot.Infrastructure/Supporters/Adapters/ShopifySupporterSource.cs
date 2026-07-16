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
/// Shopify monetization adapter (supporter-events.md §0 D2). Normalizes a Shopify order webhook
/// (<c>orders/create</c>, <c>orders/paid</c>) into a <see cref="SupporterEventDraft"/> of kind <c>merch</c>: the
/// major-unit <c>total_price</c> → minor units, <c>currency</c>, the buyer name from <c>customer.first_name/
/// last_name</c> (email fallback), the <c>line_items</c> count → <see cref="SupporterEventDraft.Quantity"/>, and
/// the order <c>id</c> → the dedup key. The inbound plane journals a dotted key→string bag, so this reads flat
/// keys. A payload with no <c>total_price</c> is not an order and is declined (the topic lives in a header the
/// journaled body does not carry, so order-shape is the discriminator).
/// </summary>
public sealed class ShopifySupporterSource : ISupporterSource
{
    public string SourceKey => "shopify";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["merch"], ConnectionMode: "webhook", RequiresOAuth: true);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = ReadFlatFields(rawPayload);

        string? total = fields.GetValueOrDefault("total_price");
        if (string.IsNullOrWhiteSpace(total))
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Not a Shopify order payload.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = ParseAmount(
            total,
            fields.GetValueOrDefault("currency")
        );

        string transactionId =
            fields.GetValueOrDefault("id")
            ?? fields.GetValueOrDefault("admin_graphql_api_id")
            ?? CompositeId(fields);

        int itemCount = CountLineItems(fields);

        SupporterEventDraft draft = new(
            Kind: "merch",
            SupporterDisplayName: ResolveBuyerName(fields),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: null,
            Quantity: itemCount > 0 ? itemCount : null,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("note"))
                ? null
                : fields.GetValueOrDefault("note"),
            IsRecurring: false,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>Buyer name: "First Last" from the customer object, then email, then "Anonymous".</summary>
    private static string ResolveBuyerName(Dictionary<string, string> fields)
    {
        string first = fields.GetValueOrDefault("customer.first_name", string.Empty).Trim();
        string last = fields.GetValueOrDefault("customer.last_name", string.Empty).Trim();
        string full = $"{first} {last}".Trim();
        if (full.Length > 0)
            return full.Length > 100 ? full[..100] : full;

        string? email =
            fields.GetValueOrDefault("email") ?? fields.GetValueOrDefault("contact_email");
        return string.IsNullOrWhiteSpace(email)
            ? "Anonymous"
            : (email.Trim().Length > 100 ? email.Trim()[..100] : email.Trim());
    }

    /// <summary>Counts the distinct <c>line_items.N.*</c> array indices in the flattened bag.</summary>
    private static int CountLineItems(Dictionary<string, string> fields)
    {
        HashSet<string> indices = new(StringComparer.Ordinal);
        foreach (string key in fields.Keys)
        {
            if (!key.StartsWith("line_items.", StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = key["line_items.".Length..];
            int dot = rest.IndexOf('.');
            indices.Add(dot < 0 ? rest : rest[..dot]);
        }
        return indices.Count;
    }

    /// <summary>
    /// Reads the payload as a flat dotted-key bag. The journaled body is already a flat object; a nested body
    /// (direct test feed) is flattened here so both shapes normalize to the same keys.
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

    /// <summary>Shopify sends money as a major-unit string ("125.00"); we store minor units (cents).</summary>
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

    /// <summary>A stable dedup id when Shopify omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("total_price", ""),
            fields.GetValueOrDefault("currency", ""),
            fields.GetValueOrDefault("email", ""),
            fields.GetValueOrDefault("created_at", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "shopify-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
