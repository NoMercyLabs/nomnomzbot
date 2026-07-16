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
/// Streamlabs tip adapter (supporter-events.md §0 D2/D3, <c>socket</c> ingress). Normalizes ONE donation item
/// from the socket's <c>donation</c> event <c>message</c> array (the profile splits the array): the major-unit
/// <c>amount</c> → minor, <c>currency</c>, <c>name</c> → the supporter, <c>message</c> → the note, and the
/// native <c>donation_id</c> (falling back to <c>id</c> / <c>_id</c>) → the dedup key.
/// </summary>
public sealed class StreamlabsSupporterSource : ISupporterSource
{
    public string SourceKey => "streamlabs";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip"], ConnectionMode: "socket", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed Streamlabs payload.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            fields.GetValueOrDefault("amount"),
            fields.GetValueOrDefault("currency")
        );

        string transactionId =
            fields.GetValueOrDefault("donation_id")
            ?? fields.GetValueOrDefault("id")
            ?? fields.GetValueOrDefault("_id")
            ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("name"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
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

    /// <summary>A stable dedup id when Streamlabs omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("name", ""),
            fields.GetValueOrDefault("amount", ""),
            fields.GetValueOrDefault("created_at", ""),
            fields.GetValueOrDefault("message", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "streamlabs-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
