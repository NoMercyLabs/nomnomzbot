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
/// DonorDrive / Extra-Life charity adapter (supporter-events.md §0 D2/D3, <c>poll</c> ingress). The poll
/// service feeds this ONE donation object from the program's public <c>/api/participants/{id}/donations</c>
/// feed: <c>displayName</c> → the supporter (donations can be anonymous), the major-unit <c>amount</c> →
/// minor units, <c>message</c> → the note, and the native <c>donationID</c> → the dedup key. The public API
/// carries no per-donation currency (the program's currency lives on its <c>/api/about</c>), so
/// <see cref="SupporterEventDraft.Currency"/> stays null rather than fabricating one.
/// </summary>
public sealed class DonordriveSupporterSource : ISupporterSource
{
    public string SourceKey => "donordrive";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["charity"], ConnectionMode: "poll", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed DonorDrive payload.",
                    "VALIDATION_FAILED"
                )
            );

        long? amountMinor = ParseMajorAmount(fields.GetValueOrDefault("amount"));

        string transactionId = fields.GetValueOrDefault("donationID") ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: "charity",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("displayName"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: null, // program-level (from /api/about), not on the donation — never fabricated
            Tier: null,
            Quantity: null,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("message"))
                ? null
                : fields.GetValueOrDefault("message"),
            IsRecurring: false,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>DonorDrive amounts are major-unit floats (25.0 = 25.00); minor units, currency unknown here.</summary>
    private static long? ParseMajorAmount(string? amount) =>
        decimal.TryParse(
            amount,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value
        )
            ? (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>A stable dedup id when DonorDrive omits the donation id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("displayName", ""),
            fields.GetValueOrDefault("amount", ""),
            fields.GetValueOrDefault("createdDateUTC", ""),
            fields.GetValueOrDefault("donorID", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "donordrive-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
