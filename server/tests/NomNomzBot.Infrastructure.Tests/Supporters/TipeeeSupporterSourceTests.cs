// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NomNomzBot.Infrastructure.Supporters.Sockets;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the TipeeeStream adapter + Socket.IO profile against the documented wire shape
/// (api.tipeeestream.com/api-doc/socketio): the endpoint is DISCOVERED via <c>GET /v2.0/site/socket</c>
/// with the API key riding <c>access_token</c> (falling back to the documented static host when discovery
/// is down), a post-connect <c>join-room</c> subscribes with the key as the room, and only
/// <c>event.type == "donation"</c> frames ingest — mapped to a <c>tip</c> with the major-unit
/// <c>parameters.amount</c> scaled to minor and the native event <c>id</c> as the dedup key.
/// </summary>
public sealed class TipeeeSupporterSourceTests
{
    private const string DonationFrame = """
        [
          {
            "appKey": "####",
            "event": {
              "id": 359821,
              "type": "donation",
              "parameters": {
                "amount": 1,
                "currency": "EUR",
                "message": "take my moneeeeey!!",
                "username": "anonym1"
              },
              "formattedAmount": "€1.00"
            }
          }
        ]
        """;

    private readonly TipeeeSupporterSource _source = new();
    private readonly TipeeeSocketProfile _profile = new();

    [Fact]
    public async Task ResolveEndpoint_UsesTheDiscoveredHost_WithTheKeyInTheQuery()
    {
        using HttpClient http = new(
            new StubHandler(
                """{ "code": 200, "message": "success", "datas": { "port": "443", "host": "https://sso-cf.tipeeestream.com" } }"""
            )
        );

        Uri endpoint = await _profile.ResolveEndpointAsync("api-key", http, CancellationToken.None);

        endpoint.Host.Should().Be("sso-cf.tipeeestream.com");
        endpoint.Query.Should().Contain("access_token=api-key");
    }

    [Fact]
    public async Task ResolveEndpoint_DiscoveryDown_FallsBackToTheDocumentedHost()
    {
        using HttpClient http = new(new StubHandler(null)); // 500s every request

        Uri endpoint = await _profile.ResolveEndpointAsync("api-key", http, CancellationToken.None);

        endpoint.Host.Should().Be("sso-cf.tipeeestream.com");
        endpoint.Query.Should().Contain("access_token=api-key");
    }

    [Fact]
    public void ConnectEmit_JoinsTheRoomKeyedByTheApiKey()
    {
        SocketIoEmit? emit = _profile.BuildConnectEmit("api-key");

        emit.Should().NotBeNull();
        emit!.EventName.Should().Be("join-room");
        emit.Payload.Should().BeEquivalentTo(new { room = "api-key", username = "nomnomzbot" });
    }

    [Fact]
    public void TranslateFrame_ForwardsOnlyDonations()
    {
        IReadOnlyList<string> donation = _profile.TranslateFrame(DonationFrame);
        donation.Should().HaveCount(1);
        donation[0].Should().Contain("take my moneeeeey!!");

        _profile
            .TranslateFrame("""[ { "appKey": "#", "event": { "type": "follow" } } ]""")
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
        draft.AmountMinor.Should().Be(100); // 1 major → 100 minor
        draft.Currency.Should().Be("EUR");
        draft.SupporterDisplayName.Should().Be("anonym1");
        draft.MessageText.Should().Be("take my moneeeeey!!");
        draft.ProviderTransactionId.Should().Be("359821");
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    /// <summary>Serves one fixed discovery body, or 500s everything when constructed with null.</summary>
    private sealed class StubHandler(string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                body is null
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    }
            );
    }
}
