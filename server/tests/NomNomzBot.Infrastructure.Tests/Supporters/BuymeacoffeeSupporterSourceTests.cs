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
/// Proves the Buy Me a Coffee adapter maps the envelope <c>type</c> onto the normalized
/// <see cref="SupporterEventDraft"/> (supporter-events.md §6): a donation → <c>tip</c> with the major-unit
/// amount scaled to minor, a membership/monthly support → <c>membership</c> (recurring, tier from the level
/// name), the supporter note honored only when BMC's own <c>note_hidden</c> privacy flag is off, and every
/// refund/update/cancellation/unmodeled shape declined so it never records as a supporter event.
/// </summary>
public sealed class BuymeacoffeeSupporterSourceTests
{
    private readonly BuymeacoffeeSupporterSource _source = new();

    private static string Donation(string noteHidden = "false", string? supporterName = "Alice") =>
        $$"""
            {
              "event_id": 1234,
              "type": "donation.created",
              "live_mode": true,
              "created": 1719825600,
              "attempt": 1,
              "data": {
                "id": 91,
                "amount": 5.5,
                "currency": "usd",
                "supporter_name": {{(supporterName is null ? "null" : $"\"{supporterName}\"")}},
                "support_note": "keep it up!",
                "note_hidden": {{noteHidden}}
              }
            }
            """;

    [Fact]
    public async Task NormalizeAsync_Donation_MapsToTip_WithMajorAmountScaledToMinor()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(Donation());

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().Be(550); // 5.5 major units → 550 minor
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Alice");
        draft.MessageText.Should().Be("keep it up!");
        draft.IsRecurring.Should().BeFalse();
        draft.ProviderTransactionId.Should().Be("1234"); // envelope event_id
    }

    [Fact]
    public async Task NormalizeAsync_NoteHidden_SuppressesTheSupporterNote()
    {
        // BMC's own privacy flag — a note the supporter marked private must never surface.
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            Donation(noteHidden: "true")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageText.Should().BeNull();
    }

    [Fact]
    public async Task NormalizeAsync_MissingSupporterName_FallsBackToAnonymous()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            Donation(supporterName: null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.SupporterDisplayName.Should().Be("Anonymous");
    }

    [Fact]
    public async Task NormalizeAsync_MembershipStarted_MapsToRecurringMembershipWithTier()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            """
            {
              "event_id": 77,
              "type": "membership.started",
              "data": {
                "amount": 10,
                "currency": "EUR",
                "supporter_name": "Bob",
                "membership_level_name": "Gold"
              }
            }
            """
        );

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("membership");
        draft.AmountMinor.Should().Be(1000);
        draft.Tier.Should().Be("Gold");
        draft.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task NormalizeAsync_RecurringDonationStarted_MapsToRecurringMembershipWithoutTier()
    {
        // Monthly support has no membership level; it is still a recurring membership-style commitment.
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            """
            {
              "event_id": 78,
              "type": "recurring_donation.started",
              "data": { "amount": 3, "currency": "USD", "supporter_name": "Cara" }
            }
            """
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("membership");
        result.Value.Tier.Should().BeNull();
        result.Value.IsRecurring.Should().BeTrue();
    }

    [Theory]
    [InlineData("donation.refunded")]
    [InlineData("membership.cancelled")]
    [InlineData("recurring_donation.updated")]
    [InlineData("extra_purchase.created")] // shop payloads are unmodeled — declined, not guessed at
    [InlineData("")]
    public async Task NormalizeAsync_NonSupporterEvents_AreDeclined(string type)
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            $$"""
            { "event_id": 9, "type": "{{type}}", "data": { "amount": 5 } }
            """
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
