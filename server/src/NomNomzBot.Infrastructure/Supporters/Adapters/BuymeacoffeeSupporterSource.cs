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
/// Buy Me a Coffee monetization adapter (supporter-events.md §0 D2). Maps the envelope <c>type</c> onto our
/// kinds — <c>donation.created</c> → <c>tip</c>; <c>membership.started</c> and
/// <c>recurring_donation.started</c> (monthly support) → <c>membership</c>, the former carrying
/// <c>membership_level_name</c> as the tier. Amounts arrive in major units (<c>data.amount</c> 5 = $5.00) →
/// minor. The supporter note is honored only when BMC's own <c>note_hidden</c> privacy flag is off. Refunds,
/// updates, cancellations, and the unmodeled shop/commission/wishlist payloads are declined —
/// <see cref="Capabilities"/> advertises only what normalizes truthfully.
/// </summary>
public sealed class BuymeacoffeeSupporterSource : ISupporterSource
{
    public string SourceKey => "buymeacoffee";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["tip", "membership"], ConnectionMode: "webhook", RequiresOAuth: false);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);
        if (fields.Count == 0)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    "Malformed Buy Me a Coffee payload.",
                    "VALIDATION_FAILED"
                )
            );

        string type = fields.GetValueOrDefault("type", string.Empty).ToLowerInvariant();
        (string Kind, bool IsRecurring)? mapped = type switch
        {
            "donation.created" => ("tip", false),
            "recurring_donation.started" => ("membership", true),
            "membership.started" => ("membership", true),
            _ => null, // refunds/updates/cancellations + unmodeled shop/commission/wishlist shapes
        };
        if (mapped is null)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    $"Unsupported Buy Me a Coffee event '{type}'.",
                    "VALIDATION_FAILED"
                )
            );
        (string kind, bool isRecurring) = mapped.Value;

        (long? amountMinor, string? currency) = SupporterAdapterHelpers.ParseMajorAmount(
            fields.GetValueOrDefault("data.amount"),
            fields.GetValueOrDefault("data.currency")
        );

        string? tier =
            type == "membership.started"
            && !string.IsNullOrWhiteSpace(fields.GetValueOrDefault("data.membership_level_name"))
                ? SupporterAdapterHelpers.Trimmed(
                    fields.GetValueOrDefault("data.membership_level_name"),
                    string.Empty,
                    50
                )
                : null;

        string transactionId =
            fields.GetValueOrDefault("event_id")
            ?? fields.GetValueOrDefault("data.psp_id")
            ?? fields.GetValueOrDefault("data.id")
            ?? CompositeId(fields);

        SupporterEventDraft draft = new(
            Kind: kind,
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("data.supporter_name"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: tier,
            Quantity: null,
            ItemsJson: null,
            MessageText: ResolveNote(fields),
            IsRecurring: isRecurring,
            ProviderTransactionId: transactionId,
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>The supporter's note — suppressed when BMC's <c>note_hidden</c> privacy flag marks it private.</summary>
    private static string? ResolveNote(Dictionary<string, string> fields)
    {
        bool hidden = string.Equals(
            fields.GetValueOrDefault("data.note_hidden"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        string? note = fields.GetValueOrDefault("data.support_note");
        return hidden || string.IsNullOrWhiteSpace(note) ? null : note;
    }

    /// <summary>A stable dedup id when BMC omits every id — a hash over the identifying fields.</summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("type", ""),
            fields.GetValueOrDefault("data.supporter_name", ""),
            fields.GetValueOrDefault("data.amount", ""),
            fields.GetValueOrDefault("created", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "buymeacoffee-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
