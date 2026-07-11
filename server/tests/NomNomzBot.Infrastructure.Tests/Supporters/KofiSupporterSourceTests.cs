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
/// Proves the Ko-fi adapter maps a real provider payload onto the normalized <see cref="SupporterEventDraft"/>
/// (supporter-events.md §6): Ko-fi's <c>type</c> → our kind, its major-unit amount → minor units, its tier +
/// subscription flags → membership fields, and its transaction id → the dedup key (composite hash when absent).
/// Every assertion is on the mapped values, and a malformed payload fails instead of yielding a junk draft.
/// </summary>
public sealed class KofiSupporterSourceTests
{
    private readonly KofiSupporterSource _source = new();

    [Fact]
    public async Task NormalizeAsync_Donation_MapsToTip_WithMinorAmountAndCurrency()
    {
        string payload = """
            {
              "type": "Donation",
              "from_name": "  Alice  ",
              "message": "gg wp",
              "amount": "5.00",
              "currency": "USD",
              "is_subscription_payment": "false",
              "kofi_transaction_id": "tx-abc-123"
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.SupporterDisplayName.Should().Be("Alice"); // trimmed
        draft.AmountMinor.Should().Be(500);
        draft.Currency.Should().Be("USD");
        draft.MessageText.Should().Be("gg wp");
        draft.IsRecurring.Should().BeFalse();
        draft.ProviderTransactionId.Should().Be("tx-abc-123");
    }

    [Fact]
    public async Task NormalizeAsync_Subscription_MapsToMembership_WithTierAndRecurring()
    {
        string payload = """
            {
              "type": "Subscription",
              "from_name": "Bob",
              "amount": "3.00",
              "currency": "EUR",
              "is_subscription_payment": "true",
              "tier_name": "Gold",
              "kofi_transaction_id": "tx-sub-9"
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("membership");
        result.Value.Tier.Should().Be("Gold");
        result.Value.IsRecurring.Should().BeTrue();
        result.Value.AmountMinor.Should().Be(300);
        result.Value.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task NormalizeAsync_ShopOrder_MapsToMerch_WithItemQuantity()
    {
        string payload = """
            {
              "type": "Shop Order",
              "from_name": "Carol",
              "amount": "20.00",
              "currency": "USD",
              "kofi_transaction_id": "tx-shop-1",
              "shop_items": [
                { "direct_link_code": "aaa", "variation_name": "S" },
                { "direct_link_code": "bbb", "variation_name": "M" }
              ]
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("merch");
        result.Value.Quantity.Should().Be(2);
        result.Value.AmountMinor.Should().Be(2000);
    }

    [Fact]
    public async Task NormalizeAsync_MissingIds_UsesADeterministicCompositeId()
    {
        string payload = """
            { "type": "Donation", "from_name": "Dave", "amount": "1.50", "timestamp": "2026-07-11T00:00:00Z" }
            """;

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(payload);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(payload);

        first.IsSuccess.Should().BeTrue();
        first.Value.ProviderTransactionId.Should().StartWith("kofi-");
        // Deterministic: the same payload hashes to the same dedup id (so a redelivery still dedups).
        second.Value.ProviderTransactionId.Should().Be(first.Value.ProviderTransactionId);
        first.Value.AmountMinor.Should().Be(150);
    }

    [Fact]
    public async Task NormalizeAsync_Malformed_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json at all");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
