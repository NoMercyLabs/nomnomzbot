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
/// Proves the DonorDrive adapter maps one public-API donation onto the normalized
/// <see cref="SupporterEventDraft"/> (supporter-events.md §6): kind <c>charity</c>, the major-unit float
/// amount scaled to minor, the donor name (with the anonymous fallback), the message, the native
/// <c>donationID</c> as the dedup key — and NO fabricated currency (the public donation object carries none;
/// the program's currency lives on its <c>/api/about</c>).
/// </summary>
public sealed class DonordriveSupporterSourceTests
{
    private readonly DonordriveSupporterSource _source = new();

    private const string Donation = """
        {
          "displayName": "Alice",
          "donorID": "d-77",
          "amount": 25.5,
          "donationID": "A1B2C3D4",
          "message": "For the kids!",
          "createdDateUTC": "2026-07-16T12:00:00.000+0000",
          "recipientName": "Children's Hospital",
          "participantID": 12345
        }
        """;

    [Fact]
    public async Task NormalizeAsync_Donation_MapsToCharity_WithMinorAmountAndNativeDedupId()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(Donation);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("charity");
        draft.AmountMinor.Should().Be(2550); // 25.5 major → 2550 minor
        draft
            .Currency.Should()
            .BeNull("the public donation object carries no currency — never fabricated");
        draft.SupporterDisplayName.Should().Be("Alice");
        draft.MessageText.Should().Be("For the kids!");
        draft.IsRecurring.Should().BeFalse();
        draft.ProviderTransactionId.Should().Be("A1B2C3D4");
    }

    [Fact]
    public async Task NormalizeAsync_AnonymousDonation_FallsBackToAnonymous()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            """{ "donationID": "X9", "amount": 5 }"""
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.SupporterDisplayName.Should().Be("Anonymous");
        result.Value.MessageText.Should().BeNull();
        result.Value.AmountMinor.Should().Be(500);
    }

    [Fact]
    public async Task NormalizeAsync_MissingDonationId_ComposesAStableDedupKey()
    {
        const string donation = """
            { "displayName": "Bob", "amount": 10, "createdDateUTC": "2026-07-16T12:00:00.000+0000" }
            """;

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(donation);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(donation);

        first.Value.ProviderTransactionId.Should().StartWith("donordrive-");
        // Stable: the same donation re-polled composes the same key, so ingest dedups it.
        second.Value.ProviderTransactionId.Should().Be(first.Value.ProviderTransactionId);
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
