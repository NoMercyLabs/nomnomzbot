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
/// Proves the Fourthwall adapter maps a real <c>DONATION</c> webhook onto the normalized
/// <see cref="SupporterEventDraft"/> (supporter-events.md §6): the major-unit
/// <c>data.amounts.total.value</c> → minor units, <c>data.username</c> → the supporter name, <c>data.id</c> →
/// the dedup key, and the event → kind <c>tip</c>. It maps the same event whether it arrives nested (a direct
/// feed) or already flattened into the dotted key→string bag the inbound plane journals, and it declines a
/// non-donation event (whose payload the adapter does not yet model) rather than emitting a junk draft.
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
    public async Task NormalizeAsync_NonDonationEvent_IsDeclined()
    {
        // Merch (ORDER_PLACED) is not modeled yet — the adapter must decline, not emit a mis-shaped draft.
        string order = """
            { "type": "ORDER_PLACED", "webhookId": "wh_2", "data": { "friendlyId": "FW-123" } }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(order);

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
