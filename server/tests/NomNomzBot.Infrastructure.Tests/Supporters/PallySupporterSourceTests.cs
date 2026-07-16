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
/// Proves the Pally.gg adapter + socket profile against the documented wire shape (docs.pally.gg): the
/// profile connects the firehose with the key in the <c>auth</c> param and extracts ONLY
/// <c>campaigntip.notify</c> payloads (pong echoes and other types are silently nothing); the source maps a
/// payload to a <c>tip</c> with <c>grossAmountInCents</c> passed straight through as minor units (no ×100),
/// USD, the display name, the message, and the native tip id as the dedup key.
/// </summary>
public sealed class PallySupporterSourceTests
{
    private const string TipFrame = """
        {
          "type": "campaigntip.notify",
          "payload": {
            "campaignTip": {
              "createdAt": "2026-07-16T18:02:33.743Z",
              "displayName": "Someone",
              "grossAmountInCents": 500,
              "id": "b1w2pjwjtb9fx0v1se9ex4n2",
              "message": "keep going!",
              "netAmountInCents": 500,
              "processingFeeInCents": 0
            },
            "page": { "slug": "pally", "title": "Pally.gg's Team Page" }
          }
        }
        """;

    private readonly PallySupporterSource _source = new();
    private readonly PallySocketProfile _profile = new();

    [Fact]
    public void BuildUri_PutsTheKeyInTheAuthParam_OnTheFirehoseChannel()
    {
        Uri uri = _profile.BuildUri("my key");

        uri.Scheme.Should().Be("wss");
        uri.Host.Should().Be("events.pally.gg");
        uri.Query.Should().Contain("auth=my%20key").And.Contain("channel=firehose");
    }

    [Fact]
    public void Keepalive_IsAPingEverySixtySeconds()
    {
        _profile.KeepaliveInterval.Should().Be(TimeSpan.FromSeconds(60));
        _profile.KeepalivePayload.Should().Be("ping");
    }

    [Fact]
    public void TranslateFrame_ExtractsTheTipPayload_AndIgnoresEverythingElse()
    {
        IReadOnlyList<string> tip = _profile.TranslateFrame(TipFrame);
        tip.Should().HaveCount(1);
        tip[0].Should().Contain("grossAmountInCents");

        _profile.TranslateFrame("pong").Should().BeEmpty();
        _profile
            .TranslateFrame("""{ "type": "campaigntip.update", "payload": {} }""")
            .Should()
            .BeEmpty();
        _profile.TranslateFrame("not json at all").Should().BeEmpty();
    }

    [Fact]
    public async Task NormalizeAsync_Tip_MapsCentsAsIsWithUsdAndNativeDedupId()
    {
        string payload = _profile.TranslateFrame(TipFrame)[0];

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().Be(500); // already minor — NOT scaled ×100
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Someone");
        draft.MessageText.Should().Be("keep going!");
        draft.IsRecurring.Should().BeFalse();
        draft.ProviderTransactionId.Should().Be("b1w2pjwjtb9fx0v1se9ex4n2");
    }

    [Fact]
    public async Task NormalizeAsync_MissingNameAndMessage_FallsBackCleanly()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            """{ "campaignTip": { "id": "t1", "grossAmountInCents": 100 } }"""
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.SupporterDisplayName.Should().Be("Anonymous");
        result.Value.MessageText.Should().BeNull();
        result.Value.AmountMinor.Should().Be(100);
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
