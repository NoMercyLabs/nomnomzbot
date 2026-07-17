// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Stripe webhook authentication + dispatch (monetization-billing.md §5.2). Verifies the signature against
/// <c>Stripe:WebhookSecret</c> before any side effect, then maps the event's <c>data.object</c> straight off
/// the wire JSON (never through SDK event classes, whose shapes shift across Stripe API versions — e.g. the
/// subscription period moving onto items, <c>invoice.subscription</c> moving under <c>parent</c>) and
/// dispatches: <c>customer.subscription.*</c> → <c>ApplyStripeSubscriptionEventAsync</c>, <c>invoice.*</c> →
/// <c>ApplyStripeInvoiceEventAsync</c>. Every other verified event type is acknowledged as a no-op. An
/// unparseable (but signed) payload returns <c>VALIDATION_FAILED</c> → 400, so Stripe retries and the failure
/// stays visible in its dashboard instead of vanishing.
/// </summary>
public sealed class StripeWebhookHandler(
    IConfiguration configuration,
    ISubscriptionService subscriptions,
    TimeProvider clock,
    ILogger<StripeWebhookHandler> logger
) : IStripeWebhookHandler
{
    public async Task<Result> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken ct = default
    )
    {
        string? secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
            return Result.Failure("Stripe webhooks are not configured.", "SERVICE_UNAVAILABLE");

        bool authentic = StripeWebhookSignature.Verify(
            payload,
            signatureHeader,
            secret,
            clock.GetUtcNow().ToUnixTimeSeconds()
        );
        if (!authentic)
            return Result.Failure("Invalid Stripe signature.", "VALIDATION_FAILED");

        JObject evt;
        try
        {
            evt = JObject.Parse(payload);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Stripe webhook: signed payload is not valid JSON");
            return Result.Failure("Malformed Stripe event payload.", "VALIDATION_FAILED");
        }

        string eventId = evt.Value<string>("id") ?? "";
        string eventType = evt.Value<string>("type") ?? "";
        if (
            evt["data"]?["object"] is not JObject obj
            || eventId.Length == 0
            || eventType.Length == 0
        )
        {
            logger.LogWarning("Stripe webhook: signed event carries no id/type/data.object");
            return Result.Failure("Malformed Stripe event payload.", "VALIDATION_FAILED");
        }

        if (eventType.StartsWith("customer.subscription.", StringComparison.Ordinal))
            return await subscriptions.ApplyStripeSubscriptionEventAsync(
                MapSubscription(eventId, eventType, obj),
                ct
            );

        if (eventType.StartsWith("invoice.", StringComparison.Ordinal))
            return await subscriptions.ApplyStripeInvoiceEventAsync(
                MapInvoice(eventId, eventType, obj),
                ct
            );

        // A verified event we don't consume (payment intents, checkout sessions, …) — acknowledge so
        // Stripe stops retrying; the subscription/invoice events above are the converging pair.
        return Result.Success();
    }

    /// <summary>
    /// Maps a <c>customer.subscription.*</c> object. The billing period reads the subscription-level
    /// <c>current_period_*</c> when present and falls back to the first item's (where newer Stripe API
    /// versions carry it); the price likewise falls back from the first item to the legacy <c>plan</c>.
    /// </summary>
    private static StripeSubscriptionEventDto MapSubscription(
        string eventId,
        string eventType,
        JObject sub
    )
    {
        JObject? firstItem = sub["items"]?["data"]?.FirstOrDefault() as JObject;
        return new StripeSubscriptionEventDto(
            eventId,
            eventType,
            sub.Value<string>("customer") ?? "",
            sub.Value<string>("id") ?? "",
            firstItem?["price"]?.Value<string>("id") ?? sub["plan"]?.Value<string>("id"),
            sub.Value<string>("status") ?? "",
            Unix(sub["current_period_start"] ?? firstItem?["current_period_start"]),
            Unix(sub["current_period_end"] ?? firstItem?["current_period_end"]),
            Unix(sub["trial_end"]),
            sub.Value<bool?>("cancel_at_period_end") ?? false
        );
    }

    /// <summary>Maps an <c>invoice.*</c> object; the subscription ref falls back from the legacy top-level
    /// field to the newer <c>parent.subscription_details.subscription</c> location.</summary>
    private static StripeInvoiceEventDto MapInvoice(string eventId, string eventType, JObject inv)
    {
        string? subscriptionId =
            AsId(inv["subscription"])
            ?? AsId(inv["parent"]?["subscription_details"]?["subscription"]);
        return new StripeInvoiceEventDto(
            eventId,
            eventType,
            inv.Value<string>("id") ?? "",
            inv.Value<string>("customer") ?? "",
            subscriptionId,
            inv.Value<string>("number"),
            inv.Value<string>("status") ?? "",
            inv.Value<int?>("amount_due") ?? 0,
            inv.Value<int?>("amount_paid") ?? 0,
            inv.Value<string>("currency") ?? "",
            Unix(inv["period_start"]),
            Unix(inv["period_end"]),
            Unix(inv["created"]) ?? DateTimeOffset.UnixEpoch,
            Unix(inv["status_transitions"]?["paid_at"]),
            inv.Value<string>("hosted_invoice_url")
        );
    }

    /// <summary>A field that is either a plain id string or an expanded object with an <c>id</c>.</summary>
    private static string? AsId(JToken? token) =>
        token switch
        {
            JObject o => o.Value<string>("id"),
            JValue { Type: JTokenType.String } v => (string?)v.Value,
            _ => null,
        };

    private static DateTimeOffset? Unix(JToken? token) =>
        token is JValue { Type: JTokenType.Integer } v
            ? DateTimeOffset.FromUnixTimeSeconds((long)v.Value!)
            : null;
}
