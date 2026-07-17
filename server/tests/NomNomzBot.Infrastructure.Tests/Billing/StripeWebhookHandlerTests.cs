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
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Infrastructure.Billing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the webhook loop CLOSES (monetization-billing.md §5.2): a signature-verified
/// <c>customer.subscription.*</c> / <c>invoice.*</c> event is mapped off the wire JSON — including the newer
/// API shapes (period on the item, subscription ref under <c>parent</c>) — and dispatched to the
/// <c>ApplyStripe*EventAsync</c> appliers; other verified types acknowledge without a dispatch, and a signed
/// but malformed payload is a 400, never a silent ack.
/// </summary>
public sealed class StripeWebhookHandlerTests
{
    private const string Secret = "whsec_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 3, 30, 0, TimeSpan.Zero);

    private static (StripeWebhookHandler Sut, ISubscriptionService Subs) Build()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret }
            )
            .Build();
        ISubscriptionService subs = Substitute.For<ISubscriptionService>();
        subs.ApplyStripeSubscriptionEventAsync(
                Arg.Any<StripeSubscriptionEventDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        subs.ApplyStripeInvoiceEventAsync(
                Arg.Any<StripeInvoiceEventDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        StripeWebhookHandler sut = new(
            config,
            subs,
            new FakeTimeProvider(Now),
            NullLogger<StripeWebhookHandler>.Instance
        );
        return (sut, subs);
    }

    /// <summary>Signs exactly as Stripe does: <c>t={ts},v1=HMACSHA256("{ts}.{payload}", secret)</c>.</summary>
    private static string Sign(string payload)
    {
        long ts = Now.ToUnixTimeSeconds();
        string v1 = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(Secret),
                Encoding.UTF8.GetBytes($"{ts}.{payload}")
            )
        );
        return $"t={ts},v1={v1}";
    }

    [Fact]
    public async Task Subscription_event_is_mapped_and_dispatched_to_the_applier()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        const string payload = """
            {"id":"evt_1","type":"customer.subscription.updated","data":{"object":{
                "id":"sub_9","customer":"cus_7","status":"active",
                "current_period_start":1767225600,"current_period_end":1769904000,
                "cancel_at_period_end":true,"trial_end":null,
                "items":{"data":[{"price":{"id":"price_pro"}}]}
            }}}
            """;

        Result result = await sut.HandleAsync(payload, Sign(payload));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await subs.Received(1)
            .ApplyStripeSubscriptionEventAsync(
                Arg.Is<StripeSubscriptionEventDto>(e =>
                    e.StripeEventId == "evt_1"
                    && e.EventType == "customer.subscription.updated"
                    && e.StripeCustomerId == "cus_7"
                    && e.StripeSubscriptionId == "sub_9"
                    && e.StripePriceId == "price_pro"
                    && e.Status == "active"
                    && e.CurrentPeriodStart == DateTimeOffset.FromUnixTimeSeconds(1767225600)
                    && e.CancelAtPeriodEnd
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Subscription_period_falls_back_to_the_item_on_the_newer_api_shape()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        // Newer Stripe API versions carry the billing period on the subscription ITEM, not the root.
        const string payload = """
            {"id":"evt_2","type":"customer.subscription.created","data":{"object":{
                "id":"sub_9","customer":"cus_7","status":"trialing",
                "items":{"data":[{"price":{"id":"price_base"},
                    "current_period_start":1767225600,"current_period_end":1769904000}]}
            }}}
            """;

        await sut.HandleAsync(payload, Sign(payload));

        await subs.Received(1)
            .ApplyStripeSubscriptionEventAsync(
                Arg.Is<StripeSubscriptionEventDto>(e =>
                    e.CurrentPeriodEnd == DateTimeOffset.FromUnixTimeSeconds(1769904000)
                    && e.StripePriceId == "price_base"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Invoice_event_is_mapped_and_dispatched_including_the_paid_timestamp()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        const string payload = """
            {"id":"evt_3","type":"invoice.payment_succeeded","data":{"object":{
                "id":"in_5","customer":"cus_7","subscription":"sub_9","number":"NOM-0007",
                "status":"paid","amount_due":799,"amount_paid":799,"currency":"eur",
                "period_start":1767225600,"period_end":1769904000,"created":1767225700,
                "status_transitions":{"paid_at":1767225800},
                "hosted_invoice_url":"https://invoice.stripe.com/i/x"
            }}}
            """;

        Result result = await sut.HandleAsync(payload, Sign(payload));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await subs.Received(1)
            .ApplyStripeInvoiceEventAsync(
                Arg.Is<StripeInvoiceEventDto>(e =>
                    e.StripeInvoiceId == "in_5"
                    && e.StripeSubscriptionId == "sub_9"
                    && e.Number == "NOM-0007"
                    && e.Status == "paid"
                    && e.AmountPaidCents == 799
                    && e.Currency == "eur"
                    && e.PaidAt == DateTimeOffset.FromUnixTimeSeconds(1767225800)
                    && e.HostedInvoiceUrl == "https://invoice.stripe.com/i/x"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Invoice_subscription_ref_falls_back_to_the_parent_location()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        // Newer Stripe API versions moved invoice.subscription under parent.subscription_details.
        const string payload = """
            {"id":"evt_4","type":"invoice.finalized","data":{"object":{
                "id":"in_6","customer":"cus_7","status":"open",
                "amount_due":399,"amount_paid":0,"currency":"eur","created":1767225700,
                "parent":{"subscription_details":{"subscription":"sub_9"}}
            }}}
            """;

        await sut.HandleAsync(payload, Sign(payload));

        await subs.Received(1)
            .ApplyStripeInvoiceEventAsync(
                Arg.Is<StripeInvoiceEventDto>(e => e.StripeSubscriptionId == "sub_9"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unconsumed_event_type_acknowledges_without_dispatching()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        const string payload = """
            {"id":"evt_5","type":"payment_intent.succeeded","data":{"object":{"id":"pi_1"}}}
            """;

        Result result = await sut.HandleAsync(payload, Sign(payload));

        result.IsSuccess.Should().BeTrue("Stripe must stop retrying event types we don't consume");
        await subs.DidNotReceive()
            .ApplyStripeSubscriptionEventAsync(
                Arg.Any<StripeSubscriptionEventDto>(),
                Arg.Any<CancellationToken>()
            );
        await subs.DidNotReceive()
            .ApplyStripeInvoiceEventAsync(
                Arg.Any<StripeInvoiceEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_signed_but_malformed_payload_is_rejected_not_silently_acked()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        const string payload = "{not json";

        Result result = await sut.HandleAsync(payload, Sign(payload));

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await subs.DidNotReceive()
            .ApplyStripeSubscriptionEventAsync(
                Arg.Any<StripeSubscriptionEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_invalid_signature_never_reaches_the_dispatcher()
    {
        (StripeWebhookHandler sut, ISubscriptionService subs) = Build();
        const string payload = """
            {"id":"evt_6","type":"customer.subscription.updated","data":{"object":{"id":"sub_9","customer":"cus_7","status":"active"}}}
            """;

        Result result = await sut.HandleAsync(payload, "t=1,v1=deadbeef");

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await subs.DidNotReceive()
            .ApplyStripeSubscriptionEventAsync(
                Arg.Any<StripeSubscriptionEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
