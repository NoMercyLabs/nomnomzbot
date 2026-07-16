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
/// StreamElements tip adapter (supporter-events.md §0 D2/D3, <c>socket</c> ingress). Normalizes one
/// <c>type == "tip"</c> event from the realtime socket: the major-unit <c>data.amount</c> → minor,
/// <c>data.currency</c>, <c>data.displayName</c> (username fallback) → the supporter,
/// <c>data.message</c> → the note, and the native <c>data.tipId</c> (falling back to the event
/// <c>_id</c>) → the dedup key.
/// </summary>
public sealed class StreamelementsSupporterSource : ISupporterSource
{
    public string SourceKey => "streamelements";

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
                    "Malformed StreamElements payload.",
                    "VALIDATION_FAILED"
                )
            );

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            fields.GetValueOrDefault("data.amount"),
            fields.GetValueOrDefault("data.currency")
        );

        string transactionId =
            fields.GetValueOrDefault("data.tipId")
            ?? fields.GetValueOrDefault("_id")
            ?? CompositeId(fields);

        string? name =
            fields.GetValueOrDefault("data.displayName")
            ?? fields.GetValueOrDefault("data.username");

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(name, "Anonymous", 100),
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

    /// <summary>A stable dedup id when StreamElements omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("data.username", ""),
            fields.GetValueOrDefault("data.amount", ""),
            fields.GetValueOrDefault("createdAt", ""),
            fields.GetValueOrDefault("data.message", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "streamelements-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
