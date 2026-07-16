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
/// Proves the Shopify adapter maps an order webhook onto the normalized <see cref="SupporterEventDraft"/>
/// (supporter-events.md §6): the major-unit <c>total_price</c> → minor units, <c>currency</c>, the buyer name
/// from <c>customer.first_name/last_name</c>, the <c>line_items</c> count → quantity, and the order <c>id</c> →
/// the dedup key, all as kind <c>merch</c>. It normalizes the same order whether nested or already flattened,
/// and declines a non-order payload (no <c>total_price</c>) instead of emitting a mis-shaped merch draft.
/// </summary>
public sealed class ShopifySupporterSourceTests
{
    private readonly ShopifySupporterSource _source = new();

    private const string NestedOrder = """
        {
          "id": 5231234567890,
          "email": "buyer@shop.com",
          "total_price": "125.00",
          "currency": "usd",
          "financial_status": "paid",
          "note": "please gift wrap",
          "customer": { "first_name": " Jane ", "last_name": "Doe", "email": "buyer@shop.com" },
          "line_items": [
            { "id": 1, "title": "Tee", "quantity": 2, "price": "25.00" },
            { "id": 2, "title": "Mug", "quantity": 1, "price": "75.00" }
          ]
        }
        """;

    [Fact]
    public async Task NormalizeAsync_Order_MapsToMerch_WithAmountNameItemCountAndDedupId()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(NestedOrder);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("merch");
        draft.AmountMinor.Should().Be(12500); // 125.00 major → 12500 minor
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Jane Doe");
        draft.Quantity.Should().Be(2); // two line items
        draft.MessageText.Should().Be("please gift wrap");
        draft.IsRecurring.Should().BeFalse();
        draft.ProviderTransactionId.Should().Be("5231234567890");
    }

    [Fact]
    public async Task NormalizeAsync_JournaledFlatBag_NormalizesIdenticallyToTheNestedBody()
    {
        string flat = """
            {
              "id": "5231234567890",
              "total_price": "125.00",
              "currency": "USD",
              "customer.first_name": "Jane",
              "customer.last_name": "Doe",
              "line_items.0.title": "Tee",
              "line_items.1.title": "Mug"
            }
            """;

        Result<SupporterEventDraft> nested = await _source.NormalizeAsync(NestedOrder);
        Result<SupporterEventDraft> journaled = await _source.NormalizeAsync(flat);

        journaled.IsSuccess.Should().BeTrue();
        journaled.Value.Kind.Should().Be("merch");
        journaled.Value.AmountMinor.Should().Be(12500);
        journaled.Value.Quantity.Should().Be(2);
        journaled.Value.SupporterDisplayName.Should().Be("Jane Doe");
        journaled.Value.ProviderTransactionId.Should().Be(nested.Value.ProviderTransactionId);
    }

    [Fact]
    public async Task NormalizeAsync_NonOrderPayload_IsDeclined()
    {
        // A non-order Shopify webhook (e.g. customers/create) has no total_price — decline, don't mis-shape.
        string customer = """
            { "id": 999, "first_name": "Sam", "last_name": "Smith", "email": "sam@shop.com" }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(customer);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NormalizeAsync_MissingIds_UsesADeterministicCompositeId()
    {
        string payload = """
            {
              "total_price": "9.99",
              "currency": "USD",
              "email": "x@shop.com",
              "created_at": "2026-07-16T00:00:00Z",
              "line_items": [ { "title": "Sticker", "quantity": 1 } ]
            }
            """;

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(payload);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(payload);

        first.IsSuccess.Should().BeTrue();
        first.Value.ProviderTransactionId.Should().StartWith("shopify-");
        second.Value.ProviderTransactionId.Should().Be(first.Value.ProviderTransactionId);
        first.Value.AmountMinor.Should().Be(999);
        first.Value.SupporterDisplayName.Should().Be("x@shop.com"); // email fallback
    }

    [Fact]
    public async Task NormalizeAsync_Malformed_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json at all");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
