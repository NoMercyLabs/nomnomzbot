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
using NomNomzBot.Infrastructure.Supporters.Sockets;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the Streamlabs adapter + Socket.IO profile against the documented wire shape (dev.streamlabs.com):
/// the profile connects with the socket token in the query on an Engine.IO-3 stream (their server is
/// Socket.IO v2), listens on the single <c>event</c> event, and splits a <c>donation</c> event's
/// <c>message</c> ARRAY into one payload per donation (platform follows/subs on the same socket are ignored);
/// the source maps one donation item to a <c>tip</c> with the major-unit amount scaled to minor, the currency,
/// the donor name/message, and the native <c>donation_id</c> as the dedup key.
/// </summary>
public sealed class StreamlabsSupporterSourceTests
{
    // The Socket.IO args array as the transport yields it: one 'event' argument carrying two donations.
    private const string DonationFrame = """
        [
          {
            "type": "donation",
            "for": "streamlabs",
            "message": [
              { "donation_id": 101, "name": "Alice", "amount": "13.37", "formatted_amount": "$13.37", "currency": "USD", "message": "gg" },
              { "donation_id": 102, "name": "Bob", "amount": "5", "formatted_amount": "$5.00", "currency": "USD", "message": null }
            ]
          }
        ]
        """;

    private readonly StreamlabsSupporterSource _source = new();
    private readonly StreamlabsSocketProfile _profile = new();

    [Fact]
    public void Profile_ConnectsWithTheSocketTokenInTheQuery_OnEngineIoThree()
    {
        Uri uri = _profile.BuildUri("tok en");

        uri.Host.Should().Be("sockets.streamlabs.com");
        uri.Query.Should().Contain("token=tok%20en");
        _profile.EngineIoVersion.Should().Be(3, "Streamlabs runs a Socket.IO v2 server");
        _profile.EventNames.Should().Equal("event");
    }

    [Fact]
    public void TranslateFrame_SplitsTheDonationArray_AndIgnoresOtherEventTypes()
    {
        IReadOnlyList<string> payloads = _profile.TranslateFrame(DonationFrame);
        payloads.Should().HaveCount(2, "each message[] item is one ingestable donation");
        payloads[0].Should().Contain("Alice");
        payloads[1].Should().Contain("Bob");

        // The same socket also carries platform follows/subs — not supporter payloads here.
        _profile
            .TranslateFrame(
                """[ { "type": "follow", "for": "twitch_account", "message": [{}] } ]"""
            )
            .Should()
            .BeEmpty();
        _profile.TranslateFrame("not json").Should().BeEmpty();
    }

    [Fact]
    public async Task NormalizeAsync_Donation_MapsToTip_WithMinorAmountAndNativeDedupId()
    {
        string payload = _profile.TranslateFrame(DonationFrame)[0];

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().Be(1337); // "13.37" major → 1337 minor
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Alice");
        draft.MessageText.Should().Be("gg");
        draft.ProviderTransactionId.Should().Be("101");
    }

    [Fact]
    public async Task NormalizeAsync_MissingIds_ComposesAStableDedupKey()
    {
        const string donation = """
            { "name": "Cara", "amount": "2.50", "currency": "EUR", "created_at": "2026-07-16 12:00:00" }
            """;

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(donation);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(donation);

        first.Value.ProviderTransactionId.Should().StartWith("streamlabs-");
        second.Value.ProviderTransactionId.Should().Be(first.Value.ProviderTransactionId);
        first.Value.AmountMinor.Should().Be(250);
        first.Value.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
