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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NomNomzBot.Infrastructure.Supporters.Sockets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the TreatStream adapter + Socket.IO profile against treatstream.com/api/details: the profile
/// exchanges the vaulted OAUTH access token for a socket token
/// (<c>POST /Oauth2/Authorize/socketToken</c> → <c>socket_token</c>) and connects
/// <c>nodeapi.treatstream.com?token=…</c>; a treat maps to a <c>tip</c> with NO fabricated amount (a treat
/// is an item — its <c>title</c> rides <c>ItemsJson</c>) and the D4 composite dedup
/// (<c>sender+receiver+createdAt+message</c>) collapses a redelivery.
/// </summary>
public sealed class TreatstreamSupporterSourceTests
{
    private const string TreatFrame = """
        [
          {
            "message": "enjoy the pizza!",
            "sender": "GenerousViewer",
            "receiver": "streamer",
            "title": "Pizza",
            "sender_type": "user",
            "receiver_type": "streamer",
            "date_created": "2026-07-16 21:00:00"
          }
        ]
        """;

    private readonly TreatstreamSupporterSource _source = new();

    [Fact]
    public async Task ResolveEndpoint_ExchangesTheAccessTokenForASocketToken()
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync(
                AuthEnums.IntegrationProvider.Treatstream,
                Arg.Any<CancellationToken>()
            )
            .Returns("ts-client");
        TreatstreamSocketProfile profile = new(credentials);
        RecordingHandler handler = new("""{ "socket_token": "sock-42" }""");
        using HttpClient http = new(handler);

        Uri endpoint = await profile.ResolveEndpointAsync(
            "oauth-access-token",
            http,
            CancellationToken.None
        );

        endpoint.Host.Should().Be("nodeapi.treatstream.com");
        endpoint.Query.Should().Contain("token=sock-42");
        // The exchange carried the client id + the OAUTH access token the runner resolved from the vault.
        handler.LastBody.Should().Contain("client_id=ts-client");
        handler.LastBody.Should().Contain("access_token=oauth-access-token");
    }

    [Fact]
    public void TranslateFrame_EveryRealTimeTreatIsIngestable()
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        TreatstreamSocketProfile profile = new(credentials);

        IReadOnlyList<string> payloads = profile.TranslateFrame(TreatFrame);
        payloads.Should().HaveCount(1);
        payloads[0].Should().Contain("Pizza");

        profile.TranslateFrame("not json").Should().BeEmpty();
        profile.EngineIoVersion.Should().Be(3);
        profile.EventNames.Should().Equal("realTimeTreat");
        profile.BuildConnectEmit("x").Should().BeNull("the socket token rides the query alone");
    }

    [Fact]
    public async Task NormalizeAsync_Treat_MapsToTip_WithNoFabricatedAmount_AndTheCompositeDedup()
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        string payload = new TreatstreamSocketProfile(credentials).TranslateFrame(TreatFrame)[0];

        Result<SupporterEventDraft> first = await _source.NormalizeAsync(payload);
        Result<SupporterEventDraft> second = await _source.NormalizeAsync(payload);

        first.IsSuccess.Should().BeTrue();
        SupporterEventDraft draft = first.Value;
        draft.Kind.Should().Be("tip");
        draft.AmountMinor.Should().BeNull("a treat is an item — TreatStream sends no money fields");
        draft.Currency.Should().BeNull();
        draft.SupporterDisplayName.Should().Be("GenerousViewer");
        draft.MessageText.Should().Be("enjoy the pizza!");
        draft.Quantity.Should().Be(1);
        draft.ItemsJson.Should().Contain("Pizza");
        // The D4 composite: a redelivered identical treat computes the same key and dedups at ingest.
        draft.ProviderTransactionId.Should().StartWith("treatstream-");
        second.Value.ProviderTransactionId.Should().Be(draft.ProviderTransactionId);
    }

    [Fact]
    public async Task NormalizeAsync_MalformedPayload_Fails()
    {
        Result<SupporterEventDraft> result = await _source.NormalizeAsync("not json");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    /// <summary>Serves one canned response and records the last form body.</summary>
    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
