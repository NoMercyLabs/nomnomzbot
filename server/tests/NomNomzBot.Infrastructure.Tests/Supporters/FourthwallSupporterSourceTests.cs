// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Infrastructure.Supporters.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the Fourthwall adapter maps all three shop kinds onto the normalized
/// <see cref="SupporterEventDraft"/> (supporter-events.md §6): <c>DONATION</c> → <c>tip</c>,
/// <c>ORDER_PLACED</c> → <c>merch</c> (documented <c>amounts.total</c>, tolerant line-item count — null when
/// the array shape is unknown, never zero), <c>SUBSCRIPTION_PURCHASED</c> → recurring <c>membership</c>
/// (flat amount + <c>nickname</c>). Same mapping whether the event arrives nested or already flattened into
/// the journaled bag; changed/expired subscriptions and unmodeled gift shapes are declined rather than
/// emitting a junk draft.
/// </summary>
public sealed class FourthwallSupporterSourceTests
{
    private readonly FourthwallSupporterSource _source = new();

    private const string NestedDonation = """
        {
          "id": "00aa4abd-5778-4199-8161-0b49b2f212e5",
          "webhookId": "wh_1",
          "shopId": "sh_c689",
          "type": "DONATION",
          "apiVersion": "V1",
          "data": {
            "id": "don_Kpcjx4HIQ1e4bTIOjX9CsA",
            "status": "OPEN",
            "email": "supporter@fourthwall.com",
            "amounts": { "total": { "value": 10, "currency": "usd" } },
            "username": "  Johnny123  ",
            "message": "Sample message"
          }
        }
        """;

    [Fact]
    public async Task NormalizeAsync_Donation_MapsToTip_WithMinorAmountNameAndDedupId()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(NestedDonation);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().Be(1000); // value 10 (major) → 1000 minor
        draft.Currency.Should().Be("USD"); // upper-cased
        draft.SupporterDisplayName.Should().Be("Johnny123"); // trimmed
        draft.MessageText.Should().Be("Sample message");
        draft.IsRecurring.Should().BeFalse();
        draft.Tier.Should().BeNull();
        draft.ProviderTransactionId.Should().Be("don_Kpcjx4HIQ1e4bTIOjX9CsA");
    }

    [Fact]
    public async Task NormalizeAsync_JournaledFlatBag_NormalizesIdenticallyToTheNestedBody()
    {
        // The inbound plane journals a flat dotted-key bag; the adapter must read the same event from it.
        string flat = """
            {
              "type": "DONATION",
              "webhookId": "wh_1",
              "data.id": "don_Kpcjx4HIQ1e4bTIOjX9CsA",
              "data.amounts.total.value": "10",
              "data.amounts.total.currency": "USD",
              "data.username": "Johnny123",
              "data.message": "Sample message"
            }
            """;

        Result<SupporterEventDraft> nested = await _source.NormalizeAsync(NestedDonation);
        Result<SupporterEventDraft> journaled = await _source.NormalizeAsync(flat);

        journaled.IsSuccess.Should().BeTrue();
        journaled.Value.Kind.Should().Be("tip");
        journaled.Value.AmountMinor.Should().Be(1000);
        journaled.Value.Currency.Should().Be("USD");
        journaled.Value.SupporterDisplayName.Should().Be("Johnny123");
        journaled.Value.ProviderTransactionId.Should().Be(nested.Value.ProviderTransactionId);
    }

    [Fact]
    public async Task NormalizeAsync_OrderPlaced_MapsToMerch_WithTotalAndTolerantItemCount()
    {
        // The documented order shape: amounts.{...,total}, username/email, id. The line-item array key is
        // unspecified in the public docs, so the count is best-effort (here: an offers[] shape → 2 items).
        string order = """
            {
              "type": "ORDER_PLACED",
              "webhookId": "wh_2",
              "data": {
                "id": "ord_9",
                "friendlyId": "FW-123",
                "status": "CONFIRMED",
                "username": "Buyer99",
                "email": "b@example.test",
                "message": "love the merch!",
                "amounts": {
                  "subtotal": { "value": 40, "currency": "USD" },
                  "shipping": { "value": 5, "currency": "USD" },
                  "total": { "value": 45, "currency": "USD" }
                },
                "offers": [
                  { "name": "Hoodie", "quantity": 1 },
                  { "name": "Mug", "quantity": 2 }
                ]
              }
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(order);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("merch");
        draft.AmountMinor.Should().Be(4500); // total 45 major → minor
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Buyer99");
        draft.Quantity.Should().Be(2, "two line items");
        draft.MessageText.Should().Be("love the merch!");
        draft.ProviderTransactionId.Should().Be("ord_9");
    }

    [Fact]
    public async Task NormalizeAsync_OrderWithUnknownItemShape_LeavesQuantityNull_NeverZero()
    {
        string order = """
            {
              "type": "ORDER_PLACED",
              "data": {
                "id": "ord_10",
                "username": "Buyer",
                "amounts": { "total": { "value": 20, "currency": "EUR" } }
              }
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(order);

        result.IsSuccess.Should().BeTrue();
        result.Value.Quantity.Should().BeNull("an uncountable order is unknown, not zero items");
        result.Value.AmountMinor.Should().Be(2000);
    }

    [Fact]
    public async Task NormalizeAsync_SubscriptionPurchased_MapsToRecurringMembership()
    {
        // The membership shape observed in the wild: a flat amount/currency + the subscriber's nickname.
        string subscription = """
            {
              "type": "SUBSCRIPTION_PURCHASED",
              "data": {
                "id": "sub_5",
                "nickname": "MemberOne",
                "amount": 5,
                "currency": "USD",
                "interval": "MONTHLY"
              }
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(subscription);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("membership");
        draft.AmountMinor.Should().Be(500);
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("MemberOne");
        draft.IsRecurring.Should().BeTrue();
        draft.ProviderTransactionId.Should().Be("sub_5");
    }

    [Theory]
    [InlineData("SUBSCRIPTION_CHANGED")]
    [InlineData("SUBSCRIPTION_EXPIRED")]
    [InlineData("GIFT_PURCHASE")]
    public async Task NormalizeAsync_NonSupporterEvents_AreDeclined(string type)
    {
        // A changed/expired subscription or an unmodeled gift shape is not a NEW supporter event.
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            $$"""{ "type": "{{type}}", "data": { "id": "x-1" } }"""
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NormalizeAsync_MissingIds_UsesADeterministicCompositeId()
    {
        string payload = """
            {
              "type": "DONATION",
              "data": {
                "amounts": { "total": { "value": 2.5, "currency": "USD" } },
                "username": "Dave",
                "createdAt": "2026-07-16T00:00:00Z"
              }
            }
            """;

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(payload);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(payload);

        first.IsSuccess.Should().BeTrue();
        first.Value.ProviderTransactionId.Should().StartWith("fourthwall-");
        // Deterministic: the same payload hashes to the same dedup id (so a redelivery still dedups).
        second.Value.ProviderTransactionId.Should().Be(first.Value.ProviderTransactionId);
        first.Value.AmountMinor.Should().Be(250); // 2.5 major → 250 minor
    }

    [Fact]
    public async Task NormalizeAsync_Malformed_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json at all");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
