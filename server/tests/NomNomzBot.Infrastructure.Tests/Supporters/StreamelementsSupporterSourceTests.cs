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
/// Proves the StreamElements adapter + Socket.IO profile against the documented wire shape
/// (StreamElements/api-docs Websockets.md): the profile connects the realtime endpoint on Engine.IO 3,
/// authenticates via the post-connect <c>authenticate {method:"jwt", token}</c> emit (the token never rides
/// the query), and forwards only <c>type == "tip"</c> events (follows/subs/cheers on the same socket are
/// EventSub's, not supporter payloads); the source maps a tip to the major-unit amount scaled to minor, the
/// currency, the display name, the message, and the native <c>tipId</c> as the dedup key.
/// </summary>
public sealed class StreamelementsSupporterSourceTests
{
    private const string TipFrame = """
        [
          {
            "_id": "65f0000000000000000000aa",
            "channel": "65f0000000000000000000bb",
            "type": "tip",
            "provider": "twitch",
            "data": {
              "tipId": "65f0000000000000000000cc",
              "username": "alice",
              "displayName": "Alice",
              "amount": 50,
              "currency": "USD",
              "message": "great stream"
            }
          }
        ]
        """;

    private readonly StreamelementsSupporterSource _source = new();
    private readonly StreamelementsSocketProfile _profile = new();

    [Fact]
    public void Profile_AuthenticatesViaThePostConnectEmit_NotTheQuery()
    {
        _profile.BuildUri("jwt-token").Query.Should().BeEmpty("the token never rides the query");
        _profile.EngineIoVersion.Should().Be(3, "StreamElements runs a Socket.IO v2 server");

        SocketIoEmit? emit = _profile.BuildConnectEmit("jwt-token");
        emit.Should().NotBeNull();
        emit!.EventName.Should().Be("authenticate");
        emit.Payload.Should().BeEquivalentTo(new { method = "jwt", token = "jwt-token" });
    }

    [Fact]
    public void TranslateFrame_ForwardsOnlyTips()
    {
        IReadOnlyList<string> tip = _profile.TranslateFrame(TipFrame);
        tip.Should().HaveCount(1);
        tip[0].Should().Contain("tipId");

        // Follows/subs/cheers ride the same socket but are platform facts EventSub already owns here.
        _profile
            .TranslateFrame("""[ { "type": "follow", "data": { "username": "x" } } ]""")
            .Should()
            .BeEmpty();
        _profile.TranslateFrame("not json").Should().BeEmpty();
    }

    [Fact]
    public async Task NormalizeAsync_Tip_MapsToTip_WithMinorAmountAndNativeDedupId()
    {
        string payload = _profile.TranslateFrame(TipFrame)[0];

        Result<SupporterEventDraft> result = await _source.NormalizeAsync(payload);

        result.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = result.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().Be(5000); // 50 major → 5000 minor
        draft.Currency.Should().Be("USD");
        draft.SupporterDisplayName.Should().Be("Alice");
        draft.MessageText.Should().Be("great stream");
        draft.ProviderTransactionId.Should().Be("65f0000000000000000000cc");
    }

    [Fact]
    public async Task NormalizeAsync_MissingTipId_FallsBackToTheEventId()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync(
            """{ "_id": "evt-9", "type": "tip", "data": { "username": "bob", "amount": 5, "currency": "EUR" } }"""
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ProviderTransactionId.Should().Be("evt-9");
        result.Value.SupporterDisplayName.Should().Be("bob"); // displayName absent → username
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
