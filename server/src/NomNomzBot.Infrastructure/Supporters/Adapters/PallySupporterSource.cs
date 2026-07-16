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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;

namespace NomNomzBot.Infrastructure.Supporters.Adapters;

/// <summary>
/// Pally.gg tip adapter (supporter-events.md §0 D2/D3, <c>ws</c> ingress). Normalizes the
/// <c>campaigntip.notify</c> payload the socket profile extracts: <c>campaignTip.grossAmountInCents</c> is
/// ALREADY minor units (docs.pally.gg: cents, USD — no ×100), <c>displayName</c> → the supporter,
/// <c>message</c> → the note, and the native <c>campaignTip.id</c> → the dedup key.
/// </summary>
public sealed class PallySupporterSource : ISupporterSource
{
    public string SourceKey => "pally";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip"], ConnectionMode: "ws", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>("Malformed Pally payload.", "VALIDATION_FAILED")
            );

        long? amountMinor = ParseCents(fields.GetValueOrDefault("campaignTip.grossAmountInCents"));

        string transactionId = fields.GetValueOrDefault("campaignTip.id") ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("campaignTip.displayName"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: "USD", // Pally amounts are USD cents (docs.pally.gg)
            Tier: null,
            Quantity: null,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("campaignTip.message"))
                ? null
                : fields.GetValueOrDefault("campaignTip.message"),
            IsRecurring: false,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>Pally amounts are already minor units (cents); parse straight through, no scaling.</summary>
    private static long? ParseCents(string? cents) =>
        long.TryParse(cents, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    /// <summary>A stable dedup id when Pally omits the tip id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("campaignTip.displayName", ""),
            fields.GetValueOrDefault("campaignTip.grossAmountInCents", ""),
            fields.GetValueOrDefault("campaignTip.createdAt", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "pally-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
