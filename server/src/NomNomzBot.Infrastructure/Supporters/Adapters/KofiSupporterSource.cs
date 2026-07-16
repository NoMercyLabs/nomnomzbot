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
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
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

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
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

        int? quantity =
            kind == "merch" ? SupporterAdapterHelpers.CountArrayItems(fields, "shop_items.") : null;

        string transactionId =
            fields.GetValueOrDefault("kofi_transaction_id")
            ?? fields.GetValueOrDefault("message_id")
            ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: kind,
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("from_name"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("tier_name"))
                ? null
                : SupporterAdapterHelpers.Trimmed(fields.GetValueOrDefault("tier_name"), "", 50),
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
}
