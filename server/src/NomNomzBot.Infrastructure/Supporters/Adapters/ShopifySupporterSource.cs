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
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);

        string? total = fields.GetValueOrDefault("total_price");
        if (string.IsNullOrWhiteSpace(total))
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Not a Shopify order payload.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            total,
            fields.GetValueOrDefault("currency")
        );

        string transactionId =
            fields.GetValueOrDefault("id")
            ?? fields.GetValueOrDefault("admin_graphql_api_id")
            ?? CompositeId(fields);

        int itemCount = SupporterAdapterHelpers.CountArrayItems(fields, "line_items.");

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
