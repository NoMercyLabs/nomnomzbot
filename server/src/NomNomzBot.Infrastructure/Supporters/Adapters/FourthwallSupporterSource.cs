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
/// Fourthwall monetization adapter (supporter-events.md §0 D2) — all three shop kinds:
/// <c>DONATION</c> → <c>tip</c>, <c>ORDER_PLACED</c> → <c>merch</c>, and
/// <c>SUBSCRIPTION_PURCHASED</c> → <c>membership</c> (a changed/expired subscription is not a new supporter
/// event and is declined). The verified common shape (docs.fourthwall.com webhook model): the major-unit
/// amount at <c>data.amounts.total.{value,currency}</c>, the supporter at <c>data.username</c> (memberships
/// say <c>data.nickname</c>), the note at <c>data.message</c>, and the dedupe id at <c>data.id</c>. Fields the
/// public docs leave unspecified (the order's line-item array key, a membership's flat amount) are probed
/// tolerantly and stay null when absent — never fabricated; the full payload rides
/// <see cref="SupporterEventDraft.PayloadJson"/> regardless.
/// </summary>
public sealed class FourthwallSupporterSource : ISupporterSource
{
    public string SourceKey => "fourthwall";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip", "merch", "membership"], ConnectionMode: "webhook", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed Fourthwall payload.",
                    "VALIDATION_FAILED"
                )
            );

        string type = fields.GetValueOrDefault("type", string.Empty).ToLowerInvariant();
        (string Kind, bool IsRecurring)? mapped = type switch
        {
            "donation" => ("tip", false),
            "order_placed" => ("merch", false),
            "subscription_purchased" => ("membership", true),
            _ => null, // updates/expiries/gift shapes are not new supporter events
        };
        if (mapped is null)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    $"Unsupported Fourthwall event '{type}'.",
                    "VALIDATION_FAILED"
                )
            );
        (string kind, bool isRecurring) = mapped.Value;

        // The documented money shape is amounts.total; a membership event has been observed with a flat
        // amount + currency instead — probe both, null when neither is present (never fabricated).
        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            fields.GetValueOrDefault("data.amounts.total.value")
                ?? fields.GetValueOrDefault("data.amount"),
            fields.GetValueOrDefault("data.amounts.total.currency")
                ?? fields.GetValueOrDefault("data.currency")
        );

        string transactionId =
            fields.GetValueOrDefault("data.id")
            ?? fields.GetValueOrDefault("webhookId")
            ?? fields.GetValueOrDefault("id")
            ?? CompositeId(fields);

        // Memberships identify the subscriber as "nickname"; shop events use "username".
        string? name =
            fields.GetValueOrDefault("data.username") ?? fields.GetValueOrDefault("data.nickname");

        // The public docs leave the order's line-item array key unspecified — count tolerantly across the
        // two shapes seen in the wild; null (not 0) when neither exists.
        int? quantity = null;
        if (kind == "merch")
        {
            int items =
                SupporterAdapterHelpers.CountArrayItems(fields, "data.offers.")
                + SupporterAdapterHelpers.CountArrayItems(fields, "data.variants.");
            quantity = items > 0 ? items : null;
        }

        SupporterEventDraft draft = new(
            Kind: kind,
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(name, "Anonymous", 100),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: null,
            Quantity: quantity,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("data.message"))
                ? null
                : fields.GetValueOrDefault("data.message"),
            IsRecurring: isRecurring,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
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
}
