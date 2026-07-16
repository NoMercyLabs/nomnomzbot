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
/// Proves the Patreon adapter maps a NEW pledge onto the normalized <see cref="SupporterEventDraft"/>
/// (supporter-events.md §6): kind <c>membership</c>, the <b>already-minor</b>
/// <c>currently_entitled_amount_cents</c> passed straight through (no ×100), <c>full_name</c> → the name, the
/// <c>included[]</c> tier title → the tier, and <c>IsRecurring</c> true. Critically it gates on the injected
/// <c>patreon.event</c>: an update or a cancellation (identical body) is declined, so a cancelled pledge never
/// records as a supporter event.
/// </summary>
public sealed class PatreonSupporterSourceTests
{
    private readonly PatreonSupporterSource _source = new();

    // The journaled flat bag the inbound adapter produces (nested JSON:API flattened + the injected event).
    private static string FlatEvent(string patreonEvent) =>
        $$"""
            {
              "patreon.event": "{{patreonEvent}}",
              "data.type": "member",
              "data.id": "member-1",
              "data.attributes.currently_entitled_amount_cents": "500",
              "data.attributes.currency": "usd",
              "data.attributes.full_name": "  Pat Ron  ",
              "data.attributes.last_charge_date": "2026-07-01T00:00:00Z",
              "included.0.type": "user",
              "included.1.type": "tier",
              "included.1.attributes.title": "Gold Tier"
            }
            """;

    [Fact]
    public async Task NormalizeAsync_NewPledge_MapsToMembership_WithCentsAsIsNameTierAndRecurring()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            FlatEvent("members:pledge:create")
        );

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("membership");
        draft.AmountMinor.Should().Be(500); // already minor — NOT scaled ×100
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Pat Ron");
        draft.Tier.Should().Be("Gold Tier");
        draft.IsRecurring.Should().BeTrue();
        draft.ProviderTransactionId.Should().StartWith("patreon-");
    }

    [Theory]
    [InlineData("members:pledge:update")]
    [InlineData("members:pledge:delete")]
    [InlineData("members:create")]
    [InlineData("")]
    public async Task NormalizeAsync_NonNewPledgeEvents_AreDeclined(string patreonEvent)
    {
        // Only a new paid pledge is a supporter event; an update/cancellation/free-follow carries the same body.
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(FlatEvent(patreonEvent));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NormalizeAsync_NoTierInIncluded_LeavesTierNull()
    {
        string noTier = """
            {
              "patreon.event": "members:pledge:create",
              "data.id": "member-2",
              "data.attributes.currently_entitled_amount_cents": "1200",
              "data.attributes.full_name": "Solo Patron",
              "included.0.type": "user"
            }
            """;

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(noTier);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().BeNull();
        result.Value.AmountMinor.Should().Be(1200);
    }

    [Fact]
    public async Task NormalizeAsync_SameCharge_DedupsToTheSameId_ButANewChargeDoesNot()
    {
        Result<SupporterEventDraft> first = await _source.NormalizeAsync(
            FlatEvent("members:pledge:create")
        );
        Result<SupporterEventDraft> redelivery = await _source.NormalizeAsync(
            FlatEvent("members:pledge:create")
        );
        // A later monthly charge = a new last_charge_date → a distinct dedup id.
        string nextMonth = FlatEvent("members:pledge:create")
            .Replace("2026-07-01T00:00:00Z", "2026-08-01T00:00:00Z");
        Result<SupporterEventDraft> newCharge = await _source.NormalizeAsync(nextMonth);

        redelivery
            .Value.ProviderTransactionId.Should()
            .Be(first.Value.ProviderTransactionId, "a redelivery of the same charge dedups");
        newCharge
            .Value.ProviderTransactionId.Should()
            .NotBe(first.Value.ProviderTransactionId, "a new charge is a fresh event");
    }

    [Fact]
    public async Task NormalizeAsync_Malformed_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json at all");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
