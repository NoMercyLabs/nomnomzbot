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
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
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

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
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
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
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
