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
/// Ko-fi monetization adapter (supporter-events.md §0 D2). Normalizes a Ko-fi payload into a
/// <see cref="SupporterEventDraft"/>. Ko-fi's <c>type</c> maps onto our four kinds — Donation/Commission →
/// <c>tip</c>, Subscription → <c>membership</c>, Shop Order → <c>merch</c>. The raw body arrives already
/// flattened (the inbound-webhook plane journals a dotted key→string bag), so this reads the flat keys.
/// </summary>
public sealed class KofiSupporterSource : ISupporterSource
{
    public string SourceKey => "kofi";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip", "membership", "merch"], ConnectionMode: "webhook", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>("Malformed Ko-fi payload.", "VALIDATION_FAILED")
            );

        string rawType = fields.GetValueOrDefault("type", "Donation");
        string kind = rawType.ToLowerInvariant() switch
        {
            "subscription" => "membership",
            "shop order" => "merch",
            _ => "tip", // Donation, Commission, and any unknown one-off → a tip.
        };

        (long? amountMinor, string? currency) = ParseAmount(
            fields.GetValueOrDefault("amount"),
            fields.GetValueOrDefault("currency")
        );

        bool isRecurring =
            string.Equals(
                fields.GetValueOrDefault("is_subscription_payment"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
            || kind == "membership";

        int? quantity = kind == "merch" ? CountShopItems(fields) : null;

        string transactionId =
            fields.GetValueOrDefault("kofi_transaction_id")
            ?? fields.GetValueOrDefault("message_id")
            ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: kind,
            SupporterDisplayName: Trimmed(fields.GetValueOrDefault("from_name"), "Anonymous", 100),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("tier_name"))
                ? null
                : Trimmed(fields.GetValueOrDefault("tier_name"), "", 50),
            Quantity: quantity,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("message"))
                ? null
                : fields.GetValueOrDefault("message"),
            IsRecurring: isRecurring,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>
    /// Reads the payload as a flat string→string bag. The journaled body is a flat JSON object; a nested Ko-fi
    /// body (direct socket/test feed) is flattened here so both shapes normalize identically.
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
                    fields[prefix] = token.ToString();
                break;
        }
    }

    /// <summary>Ko-fi sends the amount in the currency's major units ("5.00"); we store minor units (cents).</summary>
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

    /// <summary>Counts the distinct <c>shop_items.N.*</c> array indices in the flattened bag.</summary>
    private static int CountShopItems(Dictionary<string, string> fields)
    {
        HashSet<string> indices = new(StringComparer.Ordinal);
        foreach (string key in fields.Keys)
        {
            if (!key.StartsWith("shop_items.", StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = key["shop_items.".Length..];
            int dot = rest.IndexOf('.');
            indices.Add(dot < 0 ? rest : rest[..dot]);
        }
        return indices.Count;
    }

    /// <summary>A stable dedup id when Ko-fi omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("type", ""),
            fields.GetValueOrDefault("from_name", ""),
            fields.GetValueOrDefault("amount", ""),
            fields.GetValueOrDefault("timestamp", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "kofi-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string Trimmed(string? value, string fallback, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        string trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
