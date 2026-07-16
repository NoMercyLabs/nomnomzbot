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
/// TreatStream tip adapter (supporter-events.md §0 D2/D3, <c>socket</c> ingress). A treat is an ITEM, not a
/// currency amount — the payload (<c>sender</c>, <c>receiver</c>, <c>title</c>, <c>message</c>,
/// <c>date_created</c>) carries no money fields, so <see cref="SupporterEventDraft.AmountMinor"/> stays null
/// (never fabricated) and the treat's <c>title</c> rides <see cref="SupporterEventDraft.ItemsJson"/>.
/// TreatStream sends no payload id, so the dedup key is the D4 composite:
/// <c>sender + receiver + createdAt + message</c>.
/// </summary>
public sealed class TreatstreamSupporterSource : ISupporterSource
{
    public string SourceKey => "treatstream";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip"], ConnectionMode: "socket", RequiresOAuth: true);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed TreatStream payload.",
                    "VALIDATION_FAILED"
                )
            );

        string? title = fields.GetValueOrDefault("title");

        SupporterEventDraft draft = new(
            Kind: "tip",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("sender"),
                "Anonymous",
                100
            ),
            AmountMinor: null, // a treat is an item — TreatStream sends no money fields, none are invented
            Currency: null,
            Tier: null,
            Quantity: 1,
            ItemsJson: string.IsNullOrWhiteSpace(title)
                ? null
                : Newtonsoft.Json.JsonConvert.SerializeObject(new[] { title }),
            MessageText: string.IsNullOrWhiteSpace(fields.GetValueOrDefault("message"))
                ? null
                : fields.GetValueOrDefault("message"),
            IsRecurring: false,
            ProviderTransactionId: CompositeId(fields),
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>
    /// TreatStream carries no payload id — the D4 composite (<c>sender+receiver+createdAt+message</c>)
    /// collapses a redelivered treat while distinct treats stay distinct.
    /// </summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("sender", ""),
            fields.GetValueOrDefault("receiver", ""),
            fields.GetValueOrDefault("date_created", ""),
            fields.GetValueOrDefault("message", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "treatstream-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
