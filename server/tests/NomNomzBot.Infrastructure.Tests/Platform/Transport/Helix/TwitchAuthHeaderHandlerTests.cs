// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the auth handler attaches the app's <c>Client-Id</c> and the <em>resolved Twitch access token</em>
/// as the bearer — and that the bearer is the token from the request options, never a tenant Guid.
/// </summary>
public class TwitchAuthHeaderHandlerTests
{
    private const string TwitchAccessToken = "twitch-user-access-token-abc123";
    private static readonly Guid TenantGuid = Guid.Parse("0195e0d2-7777-7777-7777-000000000001");

    private static (HttpClient Client, RecordingHelixHandler Wire) BuildClient()
    {
        RecordingHelixHandler wire = new([() => new(HttpStatusCode.OK)]);
        TwitchAuthHeaderHandler auth = new(
            Options.Create(new TwitchOptions { ClientId = "client-id-xyz" })
        )
        {
            InnerHandler = wire,
        };
        return (new(auth), wire);
    }

    [Fact]
    public async Task SendAsync_AttachesClientIdAndResolvedBearer()
    {
        (HttpClient client, RecordingHelixHandler wire) = BuildClient();

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/users?id=42"
        );
        request.Options.Set(HelixRequestOptions.AccessToken, TwitchAccessToken);

        await client.SendAsync(request);

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.ClientId.Should().Be("client-id-xyz");
        sent.AuthorizationParameter.Should().Be(TwitchAccessToken);
    }

    [Fact]
    public async Task SendAsync_NeverSendsTenantGuidAsBearer()
    {
        (HttpClient client, RecordingHelixHandler wire) = BuildClient();

        // The token the transport resolved is a Twitch string token; the tenant Guid stays internal.
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/channels?broadcaster_id=99"
        );
        request.Options.Set(HelixRequestOptions.AccessToken, TwitchAccessToken);

        await client.SendAsync(request);

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.AuthorizationParameter.Should().NotBe(TenantGuid.ToString());
        sent.AuthorizationParameter.Should().Be(TwitchAccessToken);
        // Defensive: the bearer must not parse as a Guid at all.
        Guid.TryParse(sent.AuthorizationParameter, out _).Should().BeFalse();
    }
}
