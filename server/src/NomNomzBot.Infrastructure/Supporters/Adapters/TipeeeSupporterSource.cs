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
/// TipeeeStream tip adapter (supporter-events.md §0 D2/D3, <c>socket</c> ingress). Normalizes one
/// <c>donation</c> event from the SSO socket: the major-unit <c>parameters.amount</c> → minor,
/// <c>parameters.currency</c>, <c>parameters.username</c> → the supporter, <c>parameters.message</c> → the
/// note, and the native event <c>id</c> → the dedup key.
/// </summary>
public sealed class TipeeeSupporterSource : ISupporterSource
{
    public string SourceKey => "tipeee";

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
                    "Malformed Tipeee payload.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            fields.GetValueOrDefault("parameters.amount"),
            fields.GetValueOrDefault("parameters.currency")
        );

        string transactionId = fields.GetValueOrDefault("id") ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("parameters.username"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: null,
            Quantity: null,
            ItemsJson: null,
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("parameters.message"))
                ? null
                : fields.GetValueOrDefault("parameters.message"),
            IsRecurring: false,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>A stable dedup id when Tipeee omits the event id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("parameters.username", ""),
            fields.GetValueOrDefault("parameters.amount", ""),
            fields.GetValueOrDefault("parameters.message", ""),
            fields.GetValueOrDefault("created_at", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "tipeee-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
