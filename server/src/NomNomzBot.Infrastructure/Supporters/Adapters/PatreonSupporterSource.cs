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
/// Patreon monetization adapter (supporter-events.md §0 D2). Normalizes a Patreon <c>members:pledge:create</c>
/// webhook — a NEW paid pledge — into a <see cref="SupporterEventDraft"/> of kind <c>membership</c>. Gates on
/// the <c>patreon.event</c> the inbound adapter injected (the trigger is header-only, and an update/cancellation
/// carries an identical body), so a cancellation never records as a supporter event. Patreon already sends the
/// amount in <b>minor</b> units (<c>currently_entitled_amount_cents</c> — no ×100), the patron name is
/// <c>full_name</c>, the tier title is the first <c>included[]</c> entry of type <c>tier</c>, and — since there
/// is no native event id — the dedupe key is a composite of the member id + last-charge date.
/// </summary>
public sealed class PatreonSupporterSource : ISupporterSource
{
    private const string NewPledgeEvent = "members:pledge:create";

    public string SourceKey => "patreon";

    public SupporterSourceCapabilities Capabilities { get; } =
        new(Kinds: ["membership"], ConnectionMode: "webhook", RequiresOAuth: true);

    public Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> fields = SupporterAdapterHelpers.ReadFlatFields(rawPayload);

        // Only a new paid pledge is a supporter event; updates/cancellations carry the same body shape.
        string trigger = fields.GetValueOrDefault("patreon.event", string.Empty).ToLowerInvariant();
        if (trigger != NewPledgeEvent)
            return Task.FromResult(
                Result.Failure<SupporterEventDraft>(
                    $"Ignored Patreon event '{trigger}' (only {NewPledgeEvent} is a supporter event).",
                    "VALIDATION_FAILED"
                )
            );

        long? amountMinor = ParseCents(
            fields.GetValueOrDefault("data.attributes.currently_entitled_amount_cents")
        );

        string? currency = SupporterAdapterHelpers.NormalizeCurrency(
            fields.GetValueOrDefault("data.attributes.currency")
        );
        string? tier = ResolveTierTitle(fields);

        SupporterEventDraft draft = new(
            Kind: "membership",
            SupporterDisplayName: SupporterAdapterHelpers.Trimmed(
                fields.GetValueOrDefault("data.attributes.full_name"),
                "Anonymous",
                100
            ),
            AmountMinor: amountMinor,
            Currency: currency,
            Tier: tier,
            Quantity: null,
            ItemsJson: null,
            MessageText: null,
            IsRecurring: true,
            ProviderTransactionId: CompositeId(fields),
            PayloadJson: rawPayload
        );
        return Task.FromResult(Result.Success(draft));
    }

    /// <summary>The tier title = the <c>attributes.title</c> of the first <c>included[]</c> entry of type <c>tier</c>.</summary>
    private static string? ResolveTierTitle(Dictionary<string, string> fields)
    {
        foreach (KeyValuePair<string, string> field in fields)
        {
            // Match keys like "included.2.type" whose value is "tier", then read that entry's title.
            if (
                !field.Key.StartsWith("included.", StringComparison.OrdinalIgnoreCase)
                || !field.Key.EndsWith(".type", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(field.Value, "tier", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            string index = field.Key["included.".Length..^".type".Length];
            string? title = fields.GetValueOrDefault($"included.{index}.attributes.title");
            if (!string.IsNullOrWhiteSpace(title))
                return SupporterAdapterHelpers.Trimmed(title, string.Empty, 50);
        }
        return null;
    }

    /// <summary>Patreon amounts are already in minor units (cents); parse straight through, no scaling.</summary>
    private static long? ParseCents(string? cents) =>
        long.TryParse(cents, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    /// <summary>
    /// Patreon carries no per-event id, so dedupe on the member + its last charge: a redelivery of the same
    /// charge collapses, while a new monthly charge (new <c>last_charge_date</c>) is a fresh event.
    /// </summary>
    private static string CompositeId(Dictionary<string, string> fields)
    {
        string material = string.Join(
            '|',
            fields.GetValueOrDefault("data.id", ""),
            fields.GetValueOrDefault("data.attributes.last_charge_date", ""),
            fields.GetValueOrDefault("data.attributes.currently_entitled_amount_cents", "")
        );
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "patreon-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
